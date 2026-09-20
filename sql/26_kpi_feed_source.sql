/* =============================================================
   26_kpi_feed_source.sql
   Purpose : เปลี่ยนสถาปัตยกรรมจาก "คำนวณ KPI เอง" เป็น
             "ดึงค่า KPI ที่ระบบต้นทางคำนวณไว้แล้วมาใช้"

             เดิม  : CSV ลงเวลา -> FactAttendance -> calc proc 3 ตัว -> FactKpiMonthly
             ใหม่  : KPI Feed (ระบบต้นทาง) -> stg.KpiFeedRaw -> FactKpiMonthly

   Idempotent : YES (รันซ้ำได้)

   *** สคริปต์นี้ลบข้อมูลถาวร — สำรองฐานข้อมูลก่อนรัน ***

   เหตุผลของการเปลี่ยน
   ---------------------------------------------------------------
   ฝ่าย HR มีระบบที่คำนวณ KPI อยู่แล้ว การคำนวณซ้ำในระบบนี้เสี่ยงให้
   ตัวเลขสองระบบไม่ตรงกัน และไม่มีใครตอบได้ว่าอันไหนถูก ระบบนี้จึงลด
   บทบาทเหลือเพียง "รับค่ามาเก็บ + จัดรูป + ส่งรายงาน" เท่านั้น

   ยังเชื่อมต่อระบบต้นทางไม่ได้ในตอนนี้
   ---------------------------------------------------------------
   ขั้น Extract ฝั่ง C# จึงอ่านจากไฟล์ JSON จำลอง (mock-data/kpi-feed)
   ผ่าน interface เดียวกับที่ของจริงจะใช้ เปลี่ยนไปต่อ API จริงภายหลัง
   โดยไม่ต้องแก้ทั้งฐานข้อมูลและ pipeline

   สิ่งที่ยังอยู่เหมือนเดิมทุกประการ
   ---------------------------------------------------------------
   meta.KpiDefinition · meta.KpiTarget · core.FactKpiMonthly
   rpt.vw_KpiMonthly · rpt.usp_GetKpiDashboard · rpt.usp_GetKpiTrend
   -> Dashboard / Excel / PDF / อีเมล ไม่ต้องแก้แม้แต่บรรทัดเดียว

   ติดตั้งฐานข้อมูลใหม่ตั้งแต่ต้น
   ---------------------------------------------------------------
   01 02 03 04 05 06 07 08 14 21 22 23 24 26
   (ข้าม 09 10 11 11a 12 13 15 16 17 18 19 20 25 ซึ่งเป็นของเดิมที่เลิกใช้แล้ว)
   ไฟล์นี้ seed นิยาม KPI บุคลากรให้เองแล้ว จึงไม่ต้องรัน 16_hr_seed.sql
   ============================================================= */

USE KpiMonthlyReport;
GO

/* =============================================================
   ด่านตรวจ : ต้องมีนิยาม KPI หมวด HR อยู่จริง
   กันกรณีรันผิดฐานข้อมูลแล้วเหลือระบบที่ไม่มี KPI เลยสักตัว
   ============================================================= */
IF OBJECT_ID('meta.KpiDefinition') IS NULL
   OR OBJECT_ID('core.FactKpiMonthly') IS NULL
   OR OBJECT_ID('rpt.vw_KpiMonthly') IS NULL
BEGIN
    RAISERROR(N'หยุด: ไม่พบ meta.KpiDefinition / core.FactKpiMonthly / rpt.vw_KpiMonthly ฐานข้อมูลนี้ไม่ใช่ตัวที่ตั้งใจ หรือยังรัน 01-07 ไม่ครบ (ยังไม่มีอะไรถูกลบ)', 16, 1);
    SET NOEXEC ON;
END
GO

PRINT '>> ผ่านด่านตรวจ เริ่มเปลี่ยนเป็นสถาปัตยกรรม KPI Feed';
GO

/* =============================================================
   1) meta.KpiDefinition : ผูก KPI กับ "รหัสตัวชี้วัดของระบบต้นทาง"
      แทนที่จะผูกกับชื่อ stored procedure ที่ใช้คำนวณ

      CalcProcName ยังคงอยู่เพื่อความเข้ากันได้ย้อนหลัง แต่เปลี่ยนเป็น
      NULL ได้แล้ว และระบบไม่เรียกใช้มันอีกต่อไป
   ============================================================= */
IF COL_LENGTH('meta.KpiDefinition', 'SourceMetricCode') IS NULL
BEGIN
    ALTER TABLE meta.KpiDefinition ADD SourceMetricCode VARCHAR(50) NULL;
    PRINT '>> เพิ่มคอลัมน์ meta.KpiDefinition.SourceMetricCode';
END
GO

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('meta.KpiDefinition')
      AND name = 'CalcProcName' AND is_nullable = 0
)
BEGIN
    ALTER TABLE meta.KpiDefinition ALTER COLUMN CalcProcName SYSNAME NULL;
    PRINT '>> CalcProcName เป็น NULL ได้แล้ว (ระบบไม่คำนวณ KPI เองอีกต่อไป)';
END
GO

/* KPI ฝั่งการผลิตจาก 07_seed.sql ต้องไม่เหลือ
   ---------------------------------------------------------------
   ฐานข้อมูลที่รัน 25 ไปแล้วจะไม่มีอยู่แล้ว ส่วนฐานข้อมูลที่ติดตั้งใหม่
   แล้วข้าม 25 จะยังมีนิยามค้างอยู่โดยไม่มี proc คำนวณและไม่มีใน feed
   ปล่อยไว้จะทำให้ rpt.vw_ValidMonth หาเดือนที่ "ครบทุก KPI" ไม่เจอเลย */
DECLARE @deadKpi TABLE (KpiId INT PRIMARY KEY);
INSERT INTO @deadKpi (KpiId)
SELECT KpiId FROM meta.KpiDefinition
WHERE KpiCode IN ('PROD_OUTPUT','DEFECT_RATE','DOWNTIME_HRS','COST_PER_UNIT','COST_DOWN_PCT');

DELETE f FROM core.FactKpiMonthly f JOIN @deadKpi d ON d.KpiId = f.KpiId;
DELETE t FROM meta.KpiTarget      t JOIN @deadKpi d ON d.KpiId = t.KpiId;
DELETE k FROM meta.KpiDefinition  k JOIN @deadKpi d ON d.KpiId = k.KpiId;
GO

/* ค่าเริ่มต้น: รหัสตัวชี้วัดฝั่งต้นทางใช้ชื่อเดียวกับ KpiCode
   ถ้าระบบจริงใช้รหัสอื่น แก้ที่แถวนี้ทีเดียว ไม่ต้องแตะโค้ด */
UPDATE meta.KpiDefinition
SET SourceMetricCode = KpiCode
WHERE SourceMetricCode IS NULL;
GO

UPDATE meta.KpiDefinition
SET CalcProcName = NULL,
    FormulaText  = N'ดึงค่าจากระบบ KPI ต้นทาง (ระบบนี้ไม่คำนวณซ้ำ)',
    UpdatedAt    = SYSDATETIME(),
    UpdatedBy    = N'MIGRATION_26'
WHERE CategoryName = N'HR';
GO

/* นิยาม KPI บุคลากร 3 ตัว — ไฟล์นี้เป็นแหล่งความจริงแทน 16_hr_seed.sql
   ติดตั้งใหม่ก็ได้ครบ รันทับของเดิมก็ไม่ซ้ำ (MERGE บน KpiCode)

   เพิ่ม KPI ตัวใหม่ในอนาคต = insert อีก 1 แถวที่นี่ แล้วให้ต้นทางส่ง
   SourceMetricCode ตัวนั้นมาใน feed — ไม่ต้องแก้โค้ด C# และไม่ต้องเขียน proc */
MERGE meta.KpiDefinition AS t
USING (VALUES
    ('ATTENDANCE_RATE', N'Attendance Rate', N'อัตราการมางาน',   N'HR', N'%',   2, 'H', 'ATTENDANCE_RATE', 60),
    ('OVERTIME_HRS',    N'Overtime Hours',  N'ชั่วโมงล่วงเวลา', N'HR', N'hrs', 1, 'L', 'OVERTIME_HRS',    70),
    ('ABSENCE_RATE',    N'Absence Rate',    N'อัตราการขาดงาน',  N'HR', N'%',   2, 'L', 'ABSENCE_RATE',    80)
) AS s (KpiCode, KpiName, KpiNameTh, CategoryName, Unit, DecimalPlaces, Direction,
        SourceMetricCode, SortOrder)
ON t.KpiCode = s.KpiCode
WHEN MATCHED THEN UPDATE SET
    t.KpiName = s.KpiName, t.KpiNameTh = s.KpiNameTh, t.CategoryName = s.CategoryName,
    t.Unit = s.Unit, t.DecimalPlaces = s.DecimalPlaces, t.Direction = s.Direction,
    t.SourceMetricCode = s.SourceMetricCode, t.SortOrder = s.SortOrder,
    t.CalcProcName = NULL,
    t.FormulaText = N'ดึงค่าจากระบบ KPI ต้นทาง (ระบบนี้ไม่คำนวณซ้ำ)',
    t.UpdatedAt = SYSDATETIME(), t.UpdatedBy = N'MIGRATION_26'
WHEN NOT MATCHED THEN
    INSERT (KpiCode, KpiName, KpiNameTh, CategoryName, Unit, DecimalPlaces, Direction,
            CalcProcName, SourceMetricCode, FormulaText, SortOrder, CreatedBy)
    VALUES (s.KpiCode, s.KpiName, s.KpiNameTh, s.CategoryName, s.Unit, s.DecimalPlaces,
            s.Direction, NULL, s.SourceMetricCode,
            N'ดึงค่าจากระบบ KPI ต้นทาง (ระบบนี้ไม่คำนวณซ้ำ)', s.SortOrder, N'MIGRATION_26');
GO

-- กันตั้งรหัสต้นทางซ้ำกันสองตัวชี้วัด (จะทำให้ค่าเข้าผิด KPI)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_KpiDefinition_SourceMetric')
    CREATE UNIQUE INDEX UX_KpiDefinition_SourceMetric
        ON meta.KpiDefinition(SourceMetricCode)
        WHERE SourceMetricCode IS NOT NULL;
GO

/* =============================================================
   2) stg.KpiFeedRaw  <- ค่า KPI ที่ดึงมาจากระบบต้นทาง
      ทุกคอลัมน์ NVARCHAR ตามหลักการเดิมของชั้น stg
      ข้อมูลเพี้ยนจากต้นทางต้องไม่ทำให้การ import ล้มกลางคัน
   ============================================================= */
IF OBJECT_ID('stg.KpiFeedRaw') IS NULL
BEGIN
CREATE TABLE stg.KpiFeedRaw
(
    StgId            BIGINT         IDENTITY(1,1) NOT NULL,
    RunId            BIGINT         NULL,
    LoadedAt         DATETIME2(0)   NOT NULL CONSTRAINT DF_StgFeed_At DEFAULT (SYSDATETIME()),
    SourceName       NVARCHAR(260)  NULL,   -- ชื่อไฟล์ mock หรือ endpoint ของ API
    SourceLineNo     INT            NULL,

    MonthText        NVARCHAR(50)   NULL,   -- 202601 / 2026-01
    KpiCodeText      NVARCHAR(50)   NULL,   -- รหัสตัวชี้วัดฝั่งต้นทาง
    DepartmentText   NVARCHAR(100)  NULL,   -- รหัส/ชื่อแผนก หรือ ALL = ทั้งบริษัท
    ActualValueText  NVARCHAR(50)   NULL,
    TargetValueText  NVARCHAR(50)   NULL,
    NumeratorText    NVARCHAR(50)   NULL,
    DenominatorText  NVARCHAR(50)   NULL,

    IsProcessed      BIT            NOT NULL CONSTRAINT DF_StgFeed_Proc DEFAULT (0),

    CONSTRAINT PK_KpiFeedRaw PRIMARY KEY CLUSTERED (StgId)
);
CREATE INDEX IX_KpiFeedRaw_Run ON stg.KpiFeedRaw(RunId, IsProcessed);
PRINT '>> Created stg.KpiFeedRaw';
END
GO

/* =============================================================
   3) core.usp_RefreshKpi_Derived
      เติมค่าที่ "ต้องคำนวณต่อจากค่าที่ได้รับมา" ให้ครบ
        PrevMonthValue : ค่าเดือนก่อนของ KPI+แผนกเดียวกัน
        StatusFlag     : สีของตัวชี้วัด ตัดสินจากทิศทาง (H/L) เทียบเป้า

      สองอย่างนี้ไม่ใช่การคำนวณ KPI ใหม่ แต่เป็นงานนำเสนอผลที่ต้อง
      มองข้ามเดือน ระบบต้นทางส่งมาทีละเดือนจึงทำแทนให้ไม่ได้

      @FromMonthKey NULL = ทำใหม่ทุกเดือนที่มีข้อมูล
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_RefreshKpi_Derived
    @FromMonthKey INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('tempdb..#derived') IS NOT NULL DROP TABLE #derived;

    /* LAG มองข้ามเดือนที่ไม่มีข้อมูลให้เอง (เอาเดือนที่มีค่าก่อนหน้าจริง ๆ)
       จึงถูกต้องแม้ feed จะมาไม่ครบทุกเดือน */
    SELECT
        f.KpiFactKey,
        LAG(f.ActualValue) OVER (
            PARTITION BY f.KpiId, f.DepartmentId ORDER BY f.MonthKey
        ) AS PrevVal,
        f.ActualValue,
        f.TargetValue,
        k.Direction
    INTO #derived
    FROM core.FactKpiMonthly f
    JOIN meta.KpiDefinition k ON k.KpiId = f.KpiId;

    UPDATE f
    SET f.PrevMonthValue = d.PrevVal,
        f.StatusFlag =
            CASE
                WHEN d.ActualValue IS NULL OR d.TargetValue IS NULL THEN NULL
                WHEN d.Direction = 'H' THEN
                    CASE WHEN d.ActualValue >= d.TargetValue        THEN 'GREEN'
                         WHEN d.ActualValue >= d.TargetValue * 0.90 THEN 'YELLOW'
                         ELSE 'RED' END
                ELSE   -- 'L' ยิ่งน้อยยิ่งดี
                    CASE WHEN d.ActualValue <= d.TargetValue        THEN 'GREEN'
                         WHEN d.ActualValue <= d.TargetValue * 1.10 THEN 'YELLOW'
                         ELSE 'RED' END
            END
    FROM core.FactKpiMonthly f
    JOIN #derived d ON d.KpiFactKey = f.KpiFactKey
    WHERE @FromMonthKey IS NULL OR f.MonthKey >= @FromMonthKey;

    DROP TABLE #derived;
END
GO

/* =============================================================
   4) core.usp_Transform_KpiFeed
      stg.KpiFeedRaw -> core.FactKpiMonthly

      หลักการเดียวกับ Transform เดิมทุกประการ
        - แถวที่แปลงไม่ได้เข้า meta.DataRejectLog ไม่หายเงียบ ๆ
        - ซ้ำเดือน/KPI/แผนกเดิม เอาแถวหลังสุดจาก feed (ต้นทางแก้ย้อนหลังได้)
        - ลบเดือนเดิมก่อนโหลดใหม่ -> รันซ้ำข้อมูลไม่บาน
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_Transform_KpiFeed
    @RunId        BIGINT,
    @RowsWritten  INT OUTPUT,
    @RowsRejected INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @RowsWritten = 0;
    SET @RowsRejected = 0;

    BEGIN TRY
        BEGIN TRAN;

        /* ---------- 1) PARSE + resolve dimension ---------- */
        IF OBJECT_ID('tempdb..#feed') IS NOT NULL DROP TABLE #feed;

        SELECT
            r.StgId,
            /* เดือน: รับทั้ง 202601 และ 2026-01 */
            TRY_CONVERT(INT, REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(r.MonthText, N''))), '-', ''), '/', ''))
                                                              AS MonthKey,
            k.KpiId,
            CASE
                WHEN core.fn_NormalizeText(r.DepartmentText) IN (N'all', N'99', N'company', N'total')   -- fn_NormalizeText ตัด '-' ทิ้ง '-99' จึงเหลือ '99'
                    THEN -99
                ELSE COALESCE(d.DepartmentId, a.DepartmentId)
            END                                               AS DepartmentId,
            core.fn_ParseDecimal(r.ActualValueText)           AS ActualValue,
            core.fn_ParseDecimal(r.TargetValueText)           AS TargetValue,
            core.fn_ParseDecimal(r.NumeratorText)             AS Numerator,
            core.fn_ParseDecimal(r.DenominatorText)           AS Denominator,
            r.MonthText       AS RawMonth,
            r.KpiCodeText     AS RawKpi,
            r.DepartmentText  AS RawDept,
            r.ActualValueText AS RawActual
        INTO #feed
        FROM stg.KpiFeedRaw r
        /* ผูกด้วยรหัสตัวชี้วัดของต้นทาง เพิ่ม KPI ใหม่ = insert 1 แถวใน meta */
        LEFT JOIN meta.KpiDefinition k
               ON core.fn_NormalizeText(k.SourceMetricCode) = core.fn_NormalizeText(r.KpiCodeText)
              AND k.IsActive = 1
        LEFT JOIN core.DimDepartment d
               ON core.fn_NormalizeText(d.DepartmentCode) = core.fn_NormalizeText(r.DepartmentText)
              AND d.DepartmentId > 0
        LEFT JOIN core.DepartmentAlias a
               ON a.AliasText = core.fn_NormalizeText(r.DepartmentText)
        WHERE r.RunId = @RunId;

        /* ---------- 2) จำแนกแถวที่รับไม่ได้ ---------- */
        IF OBJECT_ID('tempdb..#classified') IS NOT NULL DROP TABLE #classified;

        SELECT f.*,
            CASE
                WHEN f.MonthKey IS NULL
                  OR f.MonthKey < 190001 OR f.MonthKey > 299912       THEN 'INVALID_MONTH'
                WHEN NOT EXISTS (SELECT 1 FROM core.DimDate dd
                                 WHERE dd.MonthKey = f.MonthKey)      THEN 'MONTH_OUT_OF_CALENDAR'
                WHEN f.KpiId IS NULL                                  THEN 'UNKNOWN_KPI'
                WHEN f.DepartmentId IS NULL                           THEN 'UNKNOWN_DEPARTMENT'
                WHEN f.ActualValue IS NULL                            THEN 'MISSING_ACTUAL_VALUE'
                ELSE NULL
            END AS RejectReason
        INTO #classified
        FROM #feed f;

        INSERT INTO meta.DataRejectLog (RunId, SourceTable, SourceRowId, RejectReason, RawPayload)
        SELECT @RunId, 'stg.KpiFeedRaw', c.StgId, c.RejectReason,
            CONCAT('{"Month":"',  STRING_ESCAPE(ISNULL(c.RawMonth, ''), 'json'),
                   '","Kpi":"',   STRING_ESCAPE(ISNULL(c.RawKpi, ''), 'json'),
                   '","Dept":"',  STRING_ESCAPE(ISNULL(c.RawDept, ''), 'json'),
                   '","Actual":"',STRING_ESCAPE(ISNULL(c.RawActual, ''), 'json'), '"}')
        FROM #classified c
        WHERE c.RejectReason IS NOT NULL;

        SET @RowsRejected = @@ROWCOUNT;

        /* ---------- 3) ตัดซ้ำ: เดือน+KPI+แผนก มีได้ค่าเดียว ----------
           ต้นทางส่งค่าแก้ย้อนหลังมาได้ จึงยึด "แถวหลังสุด" เป็นของจริง */
        IF OBJECT_ID('tempdb..#final') IS NOT NULL DROP TABLE #final;

        SELECT MonthKey, KpiId, DepartmentId, ActualValue, TargetValue, Numerator, Denominator
        INTO #final
        FROM (
            SELECT c.*,
                   ROW_NUMBER() OVER (
                       PARTITION BY c.MonthKey, c.KpiId, c.DepartmentId
                       ORDER BY c.StgId DESC
                   ) AS rn
            FROM #classified c
            WHERE c.RejectReason IS NULL
        ) t
        WHERE t.rn = 1;

        /* ---------- 4) เป้าหมายที่ต้นทางส่งมา เก็บลง meta.KpiTarget ด้วย ----------
           เพื่อให้หน้าเว็บและรายงานย้อนหลังอ้างเป้าเดียวกันได้เสมอ
           DepartmentId = -99 (ระดับบริษัท) เก็บเป็น NULL ตามโครงเดิมของตาราง */
        MERGE meta.KpiTarget AS t
        USING (
            SELECT DISTINCT KpiId, MonthKey,
                   NULLIF(DepartmentId, -99) AS DepartmentId,
                   TargetValue
            FROM #final
            WHERE TargetValue IS NOT NULL
        ) AS s
        ON  t.KpiId = s.KpiId
        AND t.MonthKey = s.MonthKey
        AND ISNULL(t.DepartmentId, -99) = ISNULL(s.DepartmentId, -99)
        WHEN MATCHED AND ISNULL(t.TargetValue, -999999) <> s.TargetValue
            THEN UPDATE SET t.TargetValue = s.TargetValue
        WHEN NOT MATCHED THEN
            INSERT (KpiId, MonthKey, DepartmentId, TargetValue, CreatedBy)
            VALUES (s.KpiId, s.MonthKey, s.DepartmentId, s.TargetValue, N'KPI_FEED');

        /* ---------- 5) โหลดเข้า Fact (ลบเดือนเดิมก่อน) ---------- */
        DELETE f
        FROM core.FactKpiMonthly f
        WHERE f.MonthKey IN (SELECT DISTINCT MonthKey FROM #final);

        INSERT INTO core.FactKpiMonthly
            (MonthKey, KpiId, DepartmentId, ActualValue, TargetValue, BaselineValue,
             Numerator, Denominator, SourceRunId)
        SELECT
            n.MonthKey, n.KpiId, n.DepartmentId, n.ActualValue,
            /* เป้าจาก feed ถ้าไม่ส่งมาค่อยถอยไปใช้ของที่ตั้งไว้ใน meta */
            COALESCE(n.TargetValue, tg.TargetValue),
            tg.BaselineValue,
            n.Numerator, n.Denominator, @RunId
        FROM #final n
        OUTER APPLY (
            SELECT TOP 1 t.TargetValue, t.BaselineValue
            FROM meta.KpiTarget t
            WHERE t.KpiId = n.KpiId
              AND t.MonthKey = n.MonthKey
              AND (t.DepartmentId = n.DepartmentId OR t.DepartmentId IS NULL)
            ORDER BY CASE WHEN t.DepartmentId IS NULL THEN 1 ELSE 0 END
        ) tg;

        SET @RowsWritten = @@ROWCOUNT;

        UPDATE stg.KpiFeedRaw SET IsProcessed = 1 WHERE RunId = @RunId;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH

    /* เติม PrevMonthValue / StatusFlag นอก transaction
       (อ่านข้ามเดือน ไม่ควรถือ lock ตารางไว้ตอนทำ) */
    EXEC core.usp_RefreshKpi_Derived;
END
GO

/* =============================================================
   5) rpt.vw_ValidMonth
      เดิมตัดสิน "เดือนจริง" จากจำนวนพนักงานที่ลงเวลา ซึ่งไม่มีแล้ว
      ใหม่: เดือนที่ระบบต้นทางส่งค่าระดับบริษัทมาครบทุก KPI ที่เปิดใช้อยู่
   ============================================================= */
CREATE OR ALTER VIEW rpt.vw_ValidMonth
AS
WITH active_kpi AS (
    SELECT COUNT(*) AS KpiCount
    FROM meta.KpiDefinition
    WHERE IsActive = 1
),
per_month AS (
    SELECT f.MonthKey, COUNT(DISTINCT f.KpiId) AS KpiWithValue
    FROM core.FactKpiMonthly f
    JOIN meta.KpiDefinition k ON k.KpiId = f.KpiId AND k.IsActive = 1
    WHERE f.DepartmentId = -99
      AND f.ActualValue IS NOT NULL
    GROUP BY f.MonthKey
)
SELECT m.MonthKey
FROM per_month m
CROSS JOIN active_kpi a
WHERE m.KpiWithValue >= a.KpiCount;
GO

/* =============================================================
   6) core.usp_PurgeMonth : ลบข้อมูลเดือนหนึ่งให้หมดจริง
      ตอนนี้แหล่งข้อมูลมีแค่ feed แล้ว
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_PurgeMonth
    @MonthKey INT
AS
BEGIN
    SET NOCOUNT ON;
    IF @MonthKey IS NULL
    BEGIN
        RAISERROR('MonthKey is required.', 16, 1);
        RETURN;
    END

    BEGIN TRY
        BEGIN TRAN;
            DELETE FROM core.FactKpiMonthly WHERE MonthKey = @MonthKey;
            DELETE FROM stg.KpiFeedRaw
            WHERE TRY_CONVERT(INT, REPLACE(REPLACE(MonthText, '-', ''), '/', '')) = @MonthKey;
        COMMIT TRAN;
        PRINT CONCAT('>> Purged month ', @MonthKey);
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO

/* =============================================================
   6.5) ล้างค่า KPI ที่ "เครื่องคำนวณเดิม" เคยเขียนไว้

   ค่าเดิมเหล่านี้คำนวณจากข้อมูลลงเวลาโดยระบบนี้เอง ซึ่งเลิกใช้แล้ว
   ปล่อยไว้จะปนกับค่าที่ดึงมาจากต้นทาง แล้วไม่มีทางรู้ว่าแถวไหนมาจากไหน
   เดือนที่ feed ส่งมาจะถูกเขียนทับอยู่แล้ว แต่เดือนที่ feed ไม่มี
   (เช่น เดือนผีที่ ETL เดิมเคยคำนวณค้างไว้) จะค้างอยู่ตลอดไป

   ตัดสินจาก SourceRunId: แถวที่ไม่ได้มาจากงาน ETL_KpiFeed = ของเก่า
   ============================================================= */
DELETE f
FROM core.FactKpiMonthly f
LEFT JOIN meta.EtlRunLog r ON r.RunId = f.SourceRunId
WHERE r.RunId IS NULL OR r.JobName <> N'ETL_KpiFeed';
PRINT CONCAT('>> ล้างค่า KPI ของเครื่องคำนวณเดิม ', @@ROWCOUNT, ' แถว (feed จะเติมกลับมาเอง)');
GO

/* =============================================================
   7) ถอดกลไกคำนวณ KPI เดิมออก
      ทำหลังจากสร้างของใหม่ครบแล้ว เพื่อให้ระบบไม่เคยอยู่ในสภาพ
      "ของเก่าหายแต่ของใหม่ยังไม่มา"
   ============================================================= */

/* 7.1 orchestrator + calc proc : ระบบไม่คำนวณ KPI เองอีกต่อไป */
DROP PROCEDURE IF EXISTS core.usp_RunKpi_AllMonths;
DROP PROCEDURE IF EXISTS core.usp_RunKpi_Monthly;
DROP PROCEDURE IF EXISTS core.usp_CalcKpi_AttendanceRate;
DROP PROCEDURE IF EXISTS core.usp_CalcKpi_OvertimeHours;
DROP PROCEDURE IF EXISTS core.usp_CalcKpi_AbsenceRate;
GO

/* 7.2 ETL ลงเวลา */
DROP PROCEDURE IF EXISTS core.usp_Transform_Attendance;
GO

/* 7.3 ตาราง fact/dim/staging ของฝั่งลงเวลา
       ลบ fact ก่อน dim เสมอ เพราะ fact เป็นฝั่งที่ถือ foreign key
       ไม่ลบ core.DimDate / core.DimDepartment / core.DepartmentAlias /
       stg.FileLoadHistory -> ฝั่ง feed ใช้ร่วมกันอยู่ */
DROP TABLE IF EXISTS core.FactAttendance;
GO
DROP TABLE IF EXISTS core.EmployeeAlias;
GO
DROP TABLE IF EXISTS core.DimEmployee;
DROP TABLE IF EXISTS core.DimAttendanceStatus;
DROP TABLE IF EXISTS stg.AttendanceRaw;
GO

PRINT '>> เปลี่ยนเป็นสถาปัตยกรรม KPI Feed เรียบร้อย';
GO

/* =============================================================
   8) ตรวจผล
   ============================================================= */
SELECT KpiCode, KpiNameTh, CategoryName, Unit, Direction,
       SourceMetricCode, CalcProcName, IsActive
FROM   meta.KpiDefinition
ORDER  BY SortOrder;

SELECT COUNT(*) AS KpiFactRows FROM core.FactKpiMonthly;
SELECT COUNT(*) AS FeedStagingRows FROM stg.KpiFeedRaw;
SELECT MAX(MonthKey) AS LatestValidMonth FROM rpt.vw_ValidMonth;

/* ตารางของสถาปัตยกรรมเดิมต้องไม่เหลือแล้ว (ควรได้ 0 ทุกบรรทัด) */
SELECT 'core.FactAttendance' AS RetiredObject, COUNT(*) AS StillExists
FROM sys.objects WHERE object_id = OBJECT_ID('core.FactAttendance')
UNION ALL SELECT 'stg.AttendanceRaw', COUNT(*)
FROM sys.objects WHERE object_id = OBJECT_ID('stg.AttendanceRaw')
UNION ALL SELECT 'core.usp_RunKpi_Monthly', COUNT(*)
FROM sys.objects WHERE object_id = OBJECT_ID('core.usp_RunKpi_Monthly');
GO

SET NOEXEC OFF;
GO
