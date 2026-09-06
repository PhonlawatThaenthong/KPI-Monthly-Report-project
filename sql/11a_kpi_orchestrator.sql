/* =============================================================
   11a_kpi_orchestrator.sql
   Purpose : กลไกกลางของการคำนวณ KPI — ใช้ร่วมกันทุกหมวด
             core.usp_RunKpi_Monthly   คำนวณ KPI ของเดือนที่ระบุ
             core.usp_RunKpi_AllMonths คำนวณย้อนหลังทุกเดือนที่มีข้อมูล
   Idempotent : YES (คำนวณเดือนเดิมซ้ำ ทับค่าเดิม ไม่เพิ่มแถว)

   สถาปัตยกรรม
     usp_RunKpi_Monthly
       1. สร้าง #KpiResult
       2. เรียก calc proc ทีละตัวตาม meta.KpiDefinition.CalcProcName
          เฉพาะแถวที่ IsActive = 1  -> แต่ละตัว INSERT ผลดิบลง #KpiResult
       3. เติม Target / PrevMonth / StatusFlag ให้ทีเดียว
       4. MERGE เข้า core.FactKpiMonthly

   *** ไฟล์นี้คือหัวใจของการขยายระบบ ห้ามลบ ***
   ---------------------------------------------------------------
   การเพิ่ม KPI ใหม่ทำได้โดยไม่ต้องแก้ไฟล์นี้เลย มีแค่ 3 ขั้น
     1. เพิ่มแถวใน meta.KpiDefinition (+ เป้าใน meta.KpiTarget)
     2. เขียน calc proc ตามสัญญา: รับ @MonthKey, INSERT ลง #KpiResult,
        คำนวณทั้งรายแผนกและระดับรวม (DepartmentId = -99)
     3. รัน KpiReport.Etl.exe kpi-all
   Dashboard / Excel / PDF / อีเมล จะมี KPI ตัวใหม่เองทั้งหมด
   เพราะทุกชั้นอ่านจาก meta.KpiDefinition ไม่มีที่ไหน hardcode ชื่อ KPI

   การเลิกใช้ KPI ทำได้ด้วยการตั้ง IsActive = 0 — orchestrator ข้ามให้เอง
   ============================================================= */

USE KpiMonthlyReport;
GO

/* =============================================================
   ORCHESTRATOR
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_RunKpi_Monthly
    @MonthKey    INT,
    @TriggeredBy NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @RunId   BIGINT,
            @Written INT = 0,
            @PrevMonthKey INT;

    -- หาเดือนก่อนหน้าจากปฏิทิน (ไม่คำนวณเอง กันพลาดตอนข้ามปี)
    SELECT TOP 1 @PrevMonthKey = MonthKey
    FROM core.DimDate
    WHERE MonthKey < @MonthKey
    ORDER BY MonthKey DESC;

    EXEC meta.usp_EtlRun_Start
         @JobName = N'KPI_Calculation', @MonthKey = @MonthKey,
         @TriggeredBy = @TriggeredBy, @RunId = @RunId OUTPUT;

    BEGIN TRY
        /* ---------- 1) เตรียมที่พักผล ---------- */
        IF OBJECT_ID('tempdb..#KpiResult') IS NOT NULL DROP TABLE #KpiResult;
        CREATE TABLE #KpiResult
        (
            KpiId        INT           NOT NULL,
            DepartmentId INT           NOT NULL,
            ActualValue  DECIMAL(18,4) NULL,
            Numerator    DECIMAL(18,4) NULL,
            Denominator  DECIMAL(18,4) NULL
        );

        /* ---------- 2) เรียก calc proc ทีละตัวตาม SortOrder ---------- */
        DECLARE @KpiCode VARCHAR(30), @ProcName SYSNAME, @Step INT = 0, @Sql NVARCHAR(500);

        DECLARE kpi_cur CURSOR LOCAL FAST_FORWARD FOR
            SELECT KpiCode, CalcProcName
            FROM meta.KpiDefinition
            WHERE IsActive = 1
            ORDER BY SortOrder, KpiId;

        OPEN kpi_cur;
        FETCH NEXT FROM kpi_cur INTO @KpiCode, @ProcName;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @Step += 1;

            /* *** ด่านความปลอดภัย ***
               CalcProcName มาจากตารางซึ่ง Admin แก้ได้ผ่านหน้าเว็บ
               ถ้า EXEC ตรง ๆ = เปิดช่องรันคำสั่งอะไรก็ได้
               จึงต้องยืนยันก่อนว่าเป็น stored procedure ที่มีอยู่จริงเท่านั้น */
            IF OBJECT_ID(@ProcName, 'P') IS NOT NULL
            BEGIN
                SET @Sql = N'EXEC ' + QUOTENAME(PARSENAME(@ProcName, 2))
                         + N'.' + QUOTENAME(PARSENAME(@ProcName, 1))
                         + N' @MonthKey = @p1;';
                EXEC sp_executesql @Sql, N'@p1 INT', @p1 = @MonthKey;

                EXEC meta.usp_EtlStep_Log
                     @RunId = @RunId, @StepNo = @Step, @StepName = @KpiCode,
                     @SourceName = @ProcName, @Status = 'SUCCESS';
            END
            ELSE
            BEGIN
                EXEC meta.usp_EtlStep_Log
                     @RunId = @RunId, @StepNo = @Step, @StepName = @KpiCode,
                     @SourceName = @ProcName, @Status = 'WARNING',
                     @Message = N'ไม่พบ stored procedure ที่ระบุใน CalcProcName';
            END

            FETCH NEXT FROM kpi_cur INTO @KpiCode, @ProcName;
        END

        CLOSE kpi_cur;
        DEALLOCATE kpi_cur;

        /* ---------- 3) เติม Target / PrevMonth / Status แล้วโหลดเข้า Fact ---------- */
        DELETE FROM core.FactKpiMonthly WHERE MonthKey = @MonthKey;

        INSERT INTO core.FactKpiMonthly
            (MonthKey, KpiId, DepartmentId, ActualValue, TargetValue, BaselineValue,
             PrevMonthValue, StatusFlag, Numerator, Denominator, SourceRunId)
        SELECT
            @MonthKey,
            r.KpiId,
            r.DepartmentId,
            r.ActualValue,
            t.TargetValue,
            t.BaselineValue,
            p.ActualValue,
            /* ตัดสินสี: ทิศทางของ KPI เป็นตัวกำหนด ไม่ใช่มาก/น้อยอย่างเดียว */
            CASE
                WHEN r.ActualValue IS NULL OR t.TargetValue IS NULL THEN NULL
                WHEN k.Direction = 'H' THEN
                    CASE WHEN r.ActualValue >= t.TargetValue              THEN 'GREEN'
                         WHEN r.ActualValue >= t.TargetValue * 0.90       THEN 'YELLOW'
                         ELSE 'RED' END
                ELSE  -- Direction = 'L' ยิ่งน้อยยิ่งดี
                    CASE WHEN r.ActualValue <= t.TargetValue              THEN 'GREEN'
                         WHEN r.ActualValue <= t.TargetValue * 1.10       THEN 'YELLOW'
                         ELSE 'RED' END
            END,
            r.Numerator,
            r.Denominator,
            @RunId
        FROM #KpiResult r
        JOIN meta.KpiDefinition k ON k.KpiId = r.KpiId
        OUTER APPLY (
            SELECT TOP 1 tg.TargetValue, tg.BaselineValue
            FROM meta.KpiTarget tg
            WHERE tg.KpiId = r.KpiId
              AND tg.MonthKey = @MonthKey
              AND (tg.DepartmentId = r.DepartmentId OR tg.DepartmentId IS NULL)
            ORDER BY CASE WHEN tg.DepartmentId IS NULL THEN 1 ELSE 0 END
        ) t
        OUTER APPLY (
            SELECT f.ActualValue
            FROM core.FactKpiMonthly f
            WHERE f.MonthKey = @PrevMonthKey
              AND f.KpiId = r.KpiId
              AND f.DepartmentId = r.DepartmentId
        ) p;

        SET @Written = @@ROWCOUNT;

        EXEC meta.usp_EtlRun_Finish
             @RunId = @RunId, @Status = 'SUCCESS', @RowsWritten = @Written;

        PRINT CONCAT('>> KPI ', @MonthKey, ' | RunId ', @RunId, ' | Rows ', @Written);
    END TRY
    BEGIN CATCH
        IF CURSOR_STATUS('local', 'kpi_cur') >= 0
        BEGIN
            CLOSE kpi_cur;
            DEALLOCATE kpi_cur;
        END

        DECLARE @err NVARCHAR(MAX) = ERROR_MESSAGE();
        EXEC meta.usp_EtlRun_Finish
             @RunId = @RunId, @Status = 'FAILED', @ErrorMessage = @err;
        THROW;
    END CATCH
END
GO


/* =============================================================
   ตัวช่วย : คำนวณย้อนหลังทุกเดือนที่มีข้อมูล
   ต้องไล่จากเก่าไปใหม่ เพราะ PrevMonthValue ต้องมีของเดือนก่อนแล้ว
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_RunKpi_AllMonths
    @TriggeredBy NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @m INT;
    DECLARE m_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT DISTINCT MonthKey FROM core.FactProduction
        UNION
        SELECT DISTINCT MonthKey FROM core.FactCost
        UNION
        SELECT DISTINCT MonthKey FROM core.FactDowntime
        ORDER BY 1;                 -- เก่า -> ใหม่

    OPEN m_cur;
    FETCH NEXT FROM m_cur INTO @m;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC core.usp_RunKpi_Monthly @MonthKey = @m, @TriggeredBy = @TriggeredBy;
        FETCH NEXT FROM m_cur INTO @m;
    END
    CLOSE m_cur;
    DEALLOCATE m_cur;
END
GO