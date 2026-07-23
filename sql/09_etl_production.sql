/* =============================================================
   09_etl_production.sql
   Purpose : Extract + Transform ข้อมูลการผลิต
             SourceERP.dbo.ProductionLog -> stg.ProductionRaw -> core.FactProduction
   Idempotent : YES (รันเดือนเดิมซ้ำ ยอดไม่บาน)
   ต้องรัน 00_create_source_erp.sql (จาก mock-data/erp) ก่อน
   ============================================================= */

USE KpiMonthlyReport;
GO

/* =============================================================
   EXTRACT : ต้นทาง -> Staging
   ไม่แปลงอะไรทั้งสิ้น ดึงมาดิบ ๆ ให้ครบก่อน
   ============================================================= */
CREATE OR ALTER PROCEDURE stg.usp_Extract_Production
    @RunId      BIGINT,
    @RowsRead   INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRAN;

        -- ล้าง staging ก่อนทุกรอบ = รันซ้ำได้
        -- (ระบบจริงที่ข้อมูลเยอะ ควรใช้ watermark column ดึงเฉพาะที่เปลี่ยน)
        TRUNCATE TABLE stg.ProductionRaw;

        INSERT INTO stg.ProductionRaw
            (RunId, SourceName, ProdDate, DepartmentText, ProductCode,
             ShiftText, QtyProducedText, QtyDefectText, RunHoursText, OperatorName)
        SELECT
            @RunId,
            N'SourceERP.dbo.ProductionLog',
            src.ProdDate,
            src.DeptName,
            src.ProductCode,
            src.Shift,
            src.QtyProduced,
            src.QtyDefect,
            src.RunHours,
            src.OperatorName
        FROM SourceERP.dbo.ProductionLog AS src;

        SET @RowsRead = @@ROWCOUNT;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO


/* =============================================================
   TRANSFORM : Staging -> Core
   ขั้นตอน
     1. parse ทุกคอลัมน์
     2. แยก valid / reject
     3. บันทึก reject พร้อมเหตุผล
     4. ตัดแถวซ้ำ
     5. รวมยอดตาม natural key
     6. ลบเดือนเดิมแล้ว insert ใหม่ (idempotent)
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_Transform_Production
    @RunId          BIGINT,
    @MonthKey       INT             = NULL,   -- NULL = ทำทุกเดือนที่มีใน staging
    @RowsWritten    INT             OUTPUT,
    @RowsRejected   INT             OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @RowsWritten  = 0;
    SET @RowsRejected = 0;

    BEGIN TRY
        BEGIN TRAN;

        /* ---------- 1) PARSE ---------- */
        IF OBJECT_ID('tempdb..#parsed') IS NOT NULL DROP TABLE #parsed;

        SELECT
            r.StgId,
            core.fn_ParseDate(r.ProdDate)                       AS ProdDate,
            ISNULL(a.DepartmentId, -1)                          AS DepartmentId,
            ISNULL(p.ProductId,    -1)                          AS ProductId,
            ISNULL(core.fn_ParseShift(r.ShiftText), 1)          AS ShiftNo,
            core.fn_ParseDecimal(r.QtyProducedText)             AS QtyProduced,
            -- ค่าว่าง = ยังไม่บันทึกของเสีย ตีความเป็น 0 (ตัดสินใจไว้ในเอกสาร)
            CASE WHEN LTRIM(RTRIM(ISNULL(r.QtyDefectText, N''))) = N'' THEN 0
                 ELSE core.fn_ParseDecimal(r.QtyDefectText) END AS QtyDefect,
            ISNULL(core.fn_ParseDecimal(r.RunHoursText), 0)     AS RunHours,
            -- เก็บค่าดิบไว้ทำ payload ตอน reject
            r.ProdDate        AS RawDate,
            r.DepartmentText  AS RawDept,
            r.ProductCode     AS RawProduct,
            r.QtyProducedText AS RawQty,
            r.QtyDefectText   AS RawDefect
        INTO #parsed
        FROM stg.ProductionRaw r
        LEFT JOIN core.DepartmentAlias a
               ON a.AliasText = core.fn_NormalizeText(r.DepartmentText)
        LEFT JOIN core.DimProduct p
               ON p.ProductCode = LTRIM(RTRIM(r.ProductCode))
        WHERE r.RunId = @RunId;

        /* ---------- 2) จำแนกเหตุผลที่ต้อง reject ---------- */
        IF OBJECT_ID('tempdb..#classified') IS NOT NULL DROP TABLE #classified;

        SELECT
            x.*,
            CASE
                WHEN x.ProdDate IS NULL                 THEN 'INVALID_DATE'
                WHEN d.DateKey  IS NULL                 THEN 'DATE_OUT_OF_CALENDAR'
                WHEN x.QtyProduced IS NULL              THEN 'INVALID_QTY'
                WHEN x.QtyProduced < 0                  THEN 'NEGATIVE_QTY'
                WHEN x.QtyDefect IS NULL                THEN 'INVALID_DEFECT'
                WHEN x.QtyDefect < 0                    THEN 'NEGATIVE_DEFECT'
                WHEN x.QtyDefect > x.QtyProduced        THEN 'DEFECT_GT_PRODUCED'
                ELSE NULL
            END AS RejectReason,
            d.DateKey,
            d.MonthKey
        INTO #classified
        FROM #parsed x
        LEFT JOIN core.DimDate d
               ON d.FullDate = x.ProdDate;

        /* ---------- 3) บันทึก reject ---------- */
        INSERT INTO meta.DataRejectLog (RunId, SourceTable, SourceRowId, RejectReason, RawPayload)
        SELECT
            @RunId,
            'stg.ProductionRaw',
            c.StgId,
            c.RejectReason,
            CONCAT(
                '{"ProdDate":"',    STRING_ESCAPE(ISNULL(c.RawDate,    ''), 'json'),
                '","Dept":"',       STRING_ESCAPE(ISNULL(c.RawDept,    ''), 'json'),
                '","Product":"',    STRING_ESCAPE(ISNULL(c.RawProduct, ''), 'json'),
                '","Qty":"',        STRING_ESCAPE(ISNULL(c.RawQty,     ''), 'json'),
                '","Defect":"',     STRING_ESCAPE(ISNULL(c.RawDefect,  ''), 'json'),
                '"}'
            )
        FROM #classified c
        WHERE c.RejectReason IS NOT NULL
          AND (@MonthKey IS NULL OR c.MonthKey = @MonthKey OR c.MonthKey IS NULL);

        SET @RowsRejected = @@ROWCOUNT;

        /* ---------- 4) ตัดแถวซ้ำ (ซ้ำทุกคอลัมน์ = ข้อมูลชุดเดียวกัน) ---------- */
        IF OBJECT_ID('tempdb..#deduped') IS NOT NULL DROP TABLE #deduped;

        SELECT DateKey, MonthKey, DepartmentId, ProductId, ShiftNo,
               QtyProduced, QtyDefect, RunHours
        INTO #deduped
        FROM (
            SELECT
                c.*,
                ROW_NUMBER() OVER (
                    PARTITION BY c.DateKey, c.DepartmentId, c.ProductId, c.ShiftNo,
                                 c.QtyProduced, c.QtyDefect, c.RunHours
                    ORDER BY c.StgId
                ) AS rn
            FROM #classified c
            WHERE c.RejectReason IS NULL
              AND (@MonthKey IS NULL OR c.MonthKey = @MonthKey)
        ) t
        WHERE t.rn = 1;

        /* ---------- 5) รวมยอดตาม natural key ----------
           หลังตัดซ้ำแล้ว ยังอาจมีหลายแถวชน key เดียวกันได้จริง
           เช่น สินค้า 2 รหัสที่ map ไม่เจอ ตกไปเป็น ProductId = -1 ทั้งคู่
           กรณีนี้ต้อง SUM ไม่ใช่เลือกมาแถวเดียว ไม่งั้นยอดหาย        */
        IF OBJECT_ID('tempdb..#final') IS NOT NULL DROP TABLE #final;

        SELECT
            DateKey, MonthKey, DepartmentId, ProductId, ShiftNo,
            SUM(QtyProduced) AS QtyProduced,
            SUM(QtyDefect)   AS QtyDefect,
            SUM(RunHours)    AS RunHours
        INTO #final
        FROM #deduped
        GROUP BY DateKey, MonthKey, DepartmentId, ProductId, ShiftNo;

        /* ---------- 6) โหลดเข้า Fact (ลบเดือนเดิมก่อน = idempotent) ---------- */
        DELETE f
        FROM core.FactProduction f
        WHERE f.MonthKey IN (SELECT DISTINCT MonthKey FROM #final);

        INSERT INTO core.FactProduction
            (DateKey, MonthKey, DepartmentId, ProductId, ShiftNo,
             QtyProduced, QtyDefect, RunHours, SourceRunId)
        SELECT
            DateKey, MonthKey, DepartmentId, ProductId, ShiftNo,
            QtyProduced, QtyDefect, RunHours, @RunId
        FROM #final;

        SET @RowsWritten = @@ROWCOUNT;

        UPDATE stg.ProductionRaw
        SET IsProcessed = 1
        WHERE RunId = @RunId;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO


/* =============================================================
   ORCHESTRATOR : เรียกทั้งกระบวนการ + จัดการ log
   ใช้ตัวนี้ตัวเดียวก็พอ ไม่ต้องเรียก proc ย่อยเอง
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_RunEtl_Production
    @MonthKey    INT           = NULL,
    @TriggeredBy NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @RunId    BIGINT,
            @Read     INT = 0,
            @Written  INT = 0,
            @Rejected INT = 0;

    EXEC meta.usp_EtlRun_Start
         @JobName     = N'ETL_Production',
         @MonthKey    = @MonthKey,
         @TriggeredBy = @TriggeredBy,
         @RunId       = @RunId OUTPUT;

    BEGIN TRY
        EXEC stg.usp_Extract_Production @RunId = @RunId, @RowsRead = @Read OUTPUT;

        EXEC meta.usp_EtlStep_Log
             @RunId = @RunId, @StepNo = 1, @StepName = N'Extract_Production',
             @SourceName = N'SourceERP.dbo.ProductionLog',
             @Status = 'SUCCESS', @RowsRead = @Read, @RowsWritten = @Read;

        EXEC core.usp_Transform_Production
             @RunId        = @RunId,
             @MonthKey     = @MonthKey,
             @RowsWritten  = @Written  OUTPUT,
             @RowsRejected = @Rejected OUTPUT;

        EXEC meta.usp_EtlStep_Log
             @RunId = @RunId, @StepNo = 2, @StepName = N'Transform_Production',
             @SourceName = N'stg.ProductionRaw',
             @Status = 'SUCCESS', @RowsRead = @Read,
             @RowsWritten = @Written, @RowsRejected = @Rejected;

        EXEC meta.usp_EtlRun_Finish
             @RunId = @RunId, @Status = 'SUCCESS',
             @RowsRead = @Read, @RowsWritten = @Written, @RowsRejected = @Rejected;

        PRINT CONCAT('>> RunId ', @RunId,
                     ' | Read ', @Read,
                     ' | Written ', @Written,
                     ' | Rejected ', @Rejected);
    END TRY
    BEGIN CATCH
        DECLARE @err NVARCHAR(MAX) = ERROR_MESSAGE();

        EXEC meta.usp_EtlRun_Finish
             @RunId = @RunId, @Status = 'FAILED',
             @RowsRead = @Read, @RowsWritten = @Written,
             @RowsRejected = @Rejected, @ErrorMessage = @err;

        THROW;
    END CATCH
END
GO
