/* =============================================================
   12_etl_downtime_cost.sql
   Purpose : Transform สำหรับอีก 2 แหล่ง (CSV Downtime, Excel Cost)
             ส่วน Extract ทำในฝั่ง C# Console App เพราะเป็นการอ่านไฟล์
             โปรแกรม C# จะ: bulk insert เข้า stg -> เรียก proc ในไฟล์นี้
   Idempotent : YES
   ============================================================= */

USE KpiMonthlyReport;
GO

/* -------------------------------------------------------------
   core.fn_ParsePeriodToMonthKey
   แปลงข้อความงวดบัญชีเป็น MonthKey (yyyymm)
   รองรับ: '2026-01' | '2026/01' | '01/2026' | 'Jan-26'
   ------------------------------------------------------------- */
CREATE OR ALTER FUNCTION core.fn_ParsePeriodToMonthKey (@Input NVARCHAR(50))
RETURNS INT
AS
BEGIN
    DECLARE @s NVARCHAR(50) = LTRIM(RTRIM(ISNULL(@Input, N'')));
    IF @s = N'' RETURN NULL;

    -- yyyy-MM
    IF @s LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9]'
        RETURN TRY_CONVERT(INT, REPLACE(@s, '-', ''));

    -- yyyy/MM
    IF @s LIKE '[0-9][0-9][0-9][0-9]/[0-9][0-9]'
        RETURN TRY_CONVERT(INT, REPLACE(@s, '/', ''));

    -- MM/yyyy
    IF @s LIKE '[0-9][0-9]/[0-9][0-9][0-9][0-9]'
        RETURN TRY_CONVERT(INT, RIGHT(@s, 4) + LEFT(@s, 2));

    -- MMM-yy เช่น Jan-26 (แปลงผ่าน DATE โดยเติมวันที่ 01 นำหน้า)
    DECLARE @d DATE = TRY_CONVERT(DATE, '01-' + @s, 6);
    IF @d IS NOT NULL RETURN TRY_CONVERT(INT, FORMAT(@d, 'yyyyMM'));

    RETURN NULL;
END
GO

SELECT
    core.fn_ParsePeriodToMonthKey(N'2026-01') AS T1,   -- 202601
    core.fn_ParsePeriodToMonthKey(N'01/2026') AS T2,   -- 202601
    core.fn_ParsePeriodToMonthKey(N'Jan-26')  AS T3,   -- 202601
    core.fn_ParsePeriodToMonthKey(N'ขยะ')      AS T4;   -- NULL
GO


/* =============================================================
   TRANSFORM : stg.DowntimeRaw -> core.FactDowntime
   ต่างจาก Production ตรงที่ grain คือ "1 เหตุการณ์" ไม่ใช่ยอดรวม
   จึงตัดซ้ำด้วยการเก็บแถวแรก ไม่ใช่ SUM (เหตุการณ์ซ้ำกันเป๊ะ = ข้อมูลซ้ำ
   ไม่ใช่ 2 เหตุการณ์ที่บังเอิญเหมือนกัน)
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_Transform_Downtime
    @RunId          BIGINT,
    @RowsWritten    INT OUTPUT,
    @RowsRejected   INT OUTPUT
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
            r.SourceFileName,
            core.fn_ParseDate(r.EventDate)                              AS EventDate,
            ISNULL(a.DepartmentId, -1)                                  AS DepartmentId,
            LTRIM(RTRIM(ISNULL(r.MachineCode, N'UNKNOWN')))             AS MachineCode,
            LTRIM(RTRIM(ISNULL(r.ReasonCode,  N'UNKNOWN')))             AS ReasonCode,
            TRY_CONVERT(TIME(0), NULLIF(LTRIM(RTRIM(r.StartTimeText)), N'')) AS StartTimeOnly,
            TRY_CONVERT(TIME(0), NULLIF(LTRIM(RTRIM(r.EndTimeText)),   N'')) AS EndTimeOnly,
            core.fn_ParseDecimal(r.DurationMinText)                     AS DurationTextParsed,
            r.EventDate       AS RawDate,
            r.DepartmentText  AS RawDept,
            r.StartTimeText   AS RawStart,
            r.EndTimeText     AS RawEnd,
            r.DurationMinText AS RawDuration
        INTO #parsed
        FROM stg.DowntimeRaw r
        LEFT JOIN core.DepartmentAlias a
               ON a.AliasText = core.fn_NormalizeText(r.DepartmentText)
        WHERE r.RunId = @RunId;

        /* ---------- 2) คำนวณ Start/End DateTime + Duration ----------
           รวมวันที่ (DATE) กับเวลา (TIME) เข้าด้วยกัน
           ถ้าเวลาจบ < เวลาเริ่ม ถือว่าข้ามเที่ยงคืน บวกไป 1 วัน           */
        IF OBJECT_ID('tempdb..#calc') IS NOT NULL DROP TABLE #calc;

        SELECT
            p.*,
            CASE WHEN p.EventDate IS NOT NULL AND p.StartTimeOnly IS NOT NULL
                 THEN DATEADD(SECOND, DATEDIFF(SECOND, CAST('00:00:00' AS TIME(0)), p.StartTimeOnly),
                              CAST(p.EventDate AS DATETIME2(0)))
            END AS StartDateTime,
            CASE WHEN p.EventDate IS NOT NULL AND p.EndTimeOnly IS NOT NULL
                 THEN DATEADD(SECOND, DATEDIFF(SECOND, CAST('00:00:00' AS TIME(0)), p.EndTimeOnly),
                              CAST(p.EventDate AS DATETIME2(0)))
            END AS EndDateTimeRaw
        INTO #calc
        FROM #parsed p;

        IF OBJECT_ID('tempdb..#calc2') IS NOT NULL DROP TABLE #calc2;

        SELECT
            c.*,
            CASE WHEN c.EndDateTimeRaw IS NOT NULL AND c.EndDateTimeRaw < c.StartDateTime
                 THEN DATEADD(DAY, 1, c.EndDateTimeRaw)
                 ELSE c.EndDateTimeRaw
            END AS EndDateTime
        INTO #calc2
        FROM #calc c;

        IF OBJECT_ID('tempdb..#final_calc') IS NOT NULL DROP TABLE #final_calc;

        SELECT
            c.*,
            COALESCE(
                c.DurationTextParsed,
                CASE WHEN c.StartDateTime IS NOT NULL AND c.EndDateTime IS NOT NULL
                     THEN DATEDIFF(MINUTE, c.StartDateTime, c.EndDateTime)
                END
            ) AS DurationMinutes
        INTO #final_calc
        FROM #calc2 c;

        /* ---------- 3) จำแนกเหตุผล reject ---------- */
        IF OBJECT_ID('tempdb..#classified') IS NOT NULL DROP TABLE #classified;

        SELECT
            f.*,
            d.DateKey,
            d.MonthKey,
            CASE
                WHEN f.EventDate IS NULL          THEN 'INVALID_DATE'
                WHEN d.DateKey IS NULL             THEN 'DATE_OUT_OF_CALENDAR'
                WHEN f.StartDateTime IS NULL       THEN 'INVALID_STARTTIME'
                WHEN f.DurationMinutes IS NULL     THEN 'MISSING_DURATION'
                WHEN f.DurationMinutes < 0         THEN 'NEGATIVE_DURATION'
                ELSE NULL
            END AS RejectReason
        INTO #classified
        FROM #final_calc f
        LEFT JOIN core.DimDate d ON d.FullDate = f.EventDate;

        /* ---------- 4) บันทึก reject ---------- */
        INSERT INTO meta.DataRejectLog (RunId, SourceTable, SourceRowId, RejectReason, RawPayload)
        SELECT
            @RunId, 'stg.DowntimeRaw', c.StgId, c.RejectReason,
            CONCAT(
                '{"EventDate":"', STRING_ESCAPE(ISNULL(c.RawDate,     ''), 'json'),
                '","Dept":"',     STRING_ESCAPE(ISNULL(c.RawDept,     ''), 'json'),
                '","Start":"',    STRING_ESCAPE(ISNULL(c.RawStart,    ''), 'json'),
                '","End":"',      STRING_ESCAPE(ISNULL(c.RawEnd,      ''), 'json'),
                '","Duration":"', STRING_ESCAPE(ISNULL(c.RawDuration, ''), 'json'),
                '"}'
            )
        FROM #classified c
        WHERE c.RejectReason IS NOT NULL;

        SET @RowsRejected = @@ROWCOUNT;

        /* ---------- 5) ตัดซ้ำ (เหตุการณ์ซ้ำเป๊ะ = ข้อมูลซ้ำ เก็บแถวแรกพอ) ---------- */
        IF OBJECT_ID('tempdb..#final') IS NOT NULL DROP TABLE #final;

        SELECT DateKey, MonthKey, DepartmentId, MachineCode, ReasonCode,
               StartDateTime, EndDateTime, DurationMinutes
        INTO #final
        FROM (
            SELECT c.*,
                   ROW_NUMBER() OVER (
                       PARTITION BY c.DateKey, c.DepartmentId, c.MachineCode,
                                    c.ReasonCode, c.StartDateTime
                       ORDER BY c.StgId
                   ) AS rn
            FROM #classified c
            WHERE c.RejectReason IS NULL
        ) t
        WHERE t.rn = 1;

        /* ---------- 6) โหลดเข้า Fact (ลบเดือนเดิมก่อน = idempotent) ---------- */
        DELETE fd
        FROM core.FactDowntime fd
        WHERE fd.MonthKey IN (SELECT DISTINCT MonthKey FROM #final);

        INSERT INTO core.FactDowntime
            (DateKey, MonthKey, DepartmentId, MachineCode, ReasonCode,
             StartTime, EndTime, DurationMinutes, SourceRunId)
        SELECT
            DateKey, MonthKey, DepartmentId, MachineCode, ReasonCode,
            StartDateTime, EndDateTime, DurationMinutes, @RunId
        FROM #final;

        SET @RowsWritten = @@ROWCOUNT;

        UPDATE stg.DowntimeRaw SET IsProcessed = 1 WHERE RunId = @RunId;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO


/* =============================================================
   TRANSFORM : stg.CostRaw -> core.FactCost
   จุดต่างสำคัญ: ยอดติดลบ (ใบลดหนี้) เป็นข้อมูลที่ถูกต้อง ไม่ reject
   ยอดว่างเปล่า = ยังไม่ได้ลงบัญชี ถือเป็น PENDING ไม่ใช่ข้อผิดพลาด
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_Transform_Cost
    @RunId          BIGINT,
    @RowsWritten    INT OUTPUT,
    @RowsRejected   INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @RowsWritten  = 0;
    SET @RowsRejected = 0;

    BEGIN TRY
        BEGIN TRAN;

        IF OBJECT_ID('tempdb..#parsed') IS NOT NULL DROP TABLE #parsed;

        SELECT
            r.StgId,
            core.fn_ParsePeriodToMonthKey(r.PeriodText)   AS MonthKey,
            ISNULL(a.DepartmentId, -1)                    AS DepartmentId,
            ct.CostTypeId                                 AS CostTypeId,
            core.fn_ParseDecimal(r.AmountText)             AS Amount,
            r.PeriodText   AS RawPeriod,
            r.DepartmentText AS RawDept,
            r.CostTypeText AS RawCostType,
            r.AmountText   AS RawAmount
        INTO #parsed
        FROM stg.CostRaw r
        LEFT JOIN core.DepartmentAlias a
               ON a.AliasText = core.fn_NormalizeText(r.DepartmentText)
        LEFT JOIN core.DimCostType ct
               ON core.fn_NormalizeText(ct.CostTypeCode) = core.fn_NormalizeText(r.CostTypeText)
               OR core.fn_NormalizeText(ct.CostTypeName) = core.fn_NormalizeText(r.CostTypeText)
        WHERE r.RunId = @RunId;

        IF OBJECT_ID('tempdb..#classified') IS NOT NULL DROP TABLE #classified;

        SELECT
            p.*,
            CASE
                WHEN p.MonthKey IS NULL THEN 'INVALID_PERIOD'
                WHEN NOT EXISTS (SELECT 1 FROM core.DimDate d WHERE d.MonthKey = p.MonthKey)
                     THEN 'MONTH_OUT_OF_CALENDAR'
                -- ยอดว่าง = บัญชียังไม่ปิดงวด ไม่ใช่ข้อมูลผิด แต่ยังลงต้นทุนไม่ได้
                -- บันทึกไว้เพื่อความโปร่งใส (ตรวจย้อนหลังได้ว่ามีรายการค้างเท่าไร)
                WHEN p.Amount IS NULL THEN 'PENDING_AMOUNT'
                ELSE NULL
            END AS RejectReason
        INTO #classified
        FROM #parsed p;

        INSERT INTO meta.DataRejectLog (RunId, SourceTable, SourceRowId, RejectReason, RawPayload)
        SELECT
            @RunId, 'stg.CostRaw', c.StgId, c.RejectReason,
            CONCAT(
                '{"Period":"',   STRING_ESCAPE(ISNULL(c.RawPeriod,    ''), 'json'),
                '","Dept":"',    STRING_ESCAPE(ISNULL(c.RawDept,      ''), 'json'),
                '","CostType":"',STRING_ESCAPE(ISNULL(c.RawCostType,  ''), 'json'),
                '","Amount":"',  STRING_ESCAPE(ISNULL(c.RawAmount,    ''), 'json'),
                '"}'
            )
        FROM #classified c
        WHERE c.RejectReason IS NOT NULL;

        SET @RowsRejected = @@ROWCOUNT;

        /* ประเภทต้นทุนหาไม่เจอ -> UNKNOWN (-1) ไม่ reject
           แต่ละแถวรวมกันด้วย SUM เพราะหลายบัญชีย่อยอาจตกมาเป็นประเภทเดียวกัน */
        IF OBJECT_ID('tempdb..#final') IS NOT NULL DROP TABLE #final;

        SELECT
            MonthKey, DepartmentId, ISNULL(CostTypeId, -1) AS CostTypeId,
            SUM(Amount) AS Amount
        INTO #final
        FROM #classified
        WHERE RejectReason IS NULL
        GROUP BY MonthKey, DepartmentId, ISNULL(CostTypeId, -1);

        DELETE fc
        FROM core.FactCost fc
        WHERE fc.MonthKey IN (SELECT DISTINCT MonthKey FROM #final);

        INSERT INTO core.FactCost (MonthKey, DepartmentId, CostTypeId, Amount, SourceRunId)
        SELECT MonthKey, DepartmentId, CostTypeId, Amount, @RunId
        FROM #final;

        SET @RowsWritten = @@ROWCOUNT;

        UPDATE stg.CostRaw SET IsProcessed = 1 WHERE RunId = @RunId;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO
