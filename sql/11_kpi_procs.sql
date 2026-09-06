/* =============================================================
   11_kpi_procs.sql
   Purpose : calc proc ของ KPI ฝั่งการผลิต 5 ตัว
             (PROD_OUTPUT, DEFECT_RATE, DOWNTIME_HRS,
              COST_PER_UNIT, COST_DOWN_PCT)
   Idempotent : YES

   *** ตัว orchestrator ย้ายไปอยู่ 11a_kpi_orchestrator.sql แล้ว ***
   ---------------------------------------------------------------
   เดิมสองอย่างนี้อยู่ไฟล์เดียวกัน ซึ่งอันตราย เพราะ orchestrator
   เป็นกลไกกลางที่ KPI ทุกหมวดใช้ร่วมกัน ส่วนไฟล์นี้เป็น calc proc
   ของหมวดการผลิตที่อาจถูกเลิกใช้ ถ้าวันหนึ่งลบไฟล์นี้ทั้งไฟล์
   กลไกกลางจะหายไปด้วยโดยไม่ตั้งใจ และจะเพิ่ม KPI ใหม่ไม่ได้อีกเลย

   ลำดับการรัน: 11 -> 11a (หรือ 11a ก่อนก็ได้ ไม่ผูกกัน)
   ============================================================= */

USE KpiMonthlyReport;
GO

/* -------------------------------------------------------------
   สัญญาของ calc proc ทุกตัว
     - ผู้เรียกต้องสร้าง #KpiResult มาก่อน
     - proc ทำหน้าที่เดียว: คำนวณค่าจริง แล้ว INSERT ลง #KpiResult
     - ต้องคำนวณทั้งระดับแผนก และระดับรวม (DepartmentId = -99)
     - ห้ามแตะ core.FactKpiMonthly โดยตรง

   โครงสร้าง #KpiResult
     KpiId INT, DepartmentId INT, ActualValue DECIMAL(18,4),
     Numerator DECIMAL(18,4) NULL, Denominator DECIMAL(18,4) NULL
   ------------------------------------------------------------- */


/* =============================================================
   KPI 1 : PROD_OUTPUT  -  ปริมาณการผลิต
   สูตร : SUM(QtyProduced)
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_CalcKpi_ProductionOutput
    @MonthKey INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @KpiId INT = (SELECT KpiId FROM meta.KpiDefinition WHERE KpiCode = 'PROD_OUTPUT');
    IF @KpiId IS NULL RETURN;

    -- ระดับแผนก
    INSERT INTO #KpiResult (KpiId, DepartmentId, ActualValue, Numerator, Denominator)
    SELECT @KpiId, f.DepartmentId, SUM(f.QtyProduced), SUM(f.QtyProduced), NULL
    FROM core.FactProduction f
    WHERE f.MonthKey = @MonthKey
    GROUP BY f.DepartmentId;

    -- ระดับรวมทั้งบริษัท
    INSERT INTO #KpiResult (KpiId, DepartmentId, ActualValue, Numerator, Denominator)
    SELECT @KpiId, -99, SUM(f.QtyProduced), SUM(f.QtyProduced), NULL
    FROM core.FactProduction f
    WHERE f.MonthKey = @MonthKey
    HAVING COUNT(*) > 0;
END
GO


/* =============================================================
   KPI 2 : DEFECT_RATE  -  อัตราของเสีย (%)
   สูตร : SUM(QtyDefect) / SUM(QtyProduced) * 100

   *** จุดที่พลาดง่ายมาก ***
   ระดับรวมต้องคำนวณจาก SUM(defect)/SUM(produced)
   ห้ามเอาอัตราของแต่ละแผนกมาหาค่าเฉลี่ย เพราะแต่ละแผนกผลิตไม่เท่ากัน
   ค่าเฉลี่ยของอัตราส่วน != อัตราส่วนของผลรวม
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_CalcKpi_DefectRate
    @MonthKey INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @KpiId INT = (SELECT KpiId FROM meta.KpiDefinition WHERE KpiCode = 'DEFECT_RATE');
    IF @KpiId IS NULL RETURN;

    INSERT INTO #KpiResult (KpiId, DepartmentId, ActualValue, Numerator, Denominator)
    SELECT
        @KpiId,
        f.DepartmentId,
        SUM(f.QtyDefect) * 100.0 / NULLIF(SUM(f.QtyProduced), 0),
        SUM(f.QtyDefect),
        SUM(f.QtyProduced)
    FROM core.FactProduction f
    WHERE f.MonthKey = @MonthKey
    GROUP BY f.DepartmentId;

    INSERT INTO #KpiResult (KpiId, DepartmentId, ActualValue, Numerator, Denominator)
    SELECT
        @KpiId, -99,
        SUM(f.QtyDefect) * 100.0 / NULLIF(SUM(f.QtyProduced), 0),
        SUM(f.QtyDefect),
        SUM(f.QtyProduced)
    FROM core.FactProduction f
    WHERE f.MonthKey = @MonthKey
    HAVING COUNT(*) > 0;
END
GO


/* =============================================================
   KPI 3 : DOWNTIME_HRS  -  ชั่วโมงเครื่องหยุด
   สูตร : SUM(DurationMinutes) / 60
   ต้องโหลด core.FactDowntime ก่อน (มาจากไฟล์ CSV -> ETL Console App)
   ตอนนี้ยังไม่มีข้อมูล proc จะไม่ insert อะไร ซึ่งถูกต้อง
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_CalcKpi_DowntimeHours
    @MonthKey INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @KpiId INT = (SELECT KpiId FROM meta.KpiDefinition WHERE KpiCode = 'DOWNTIME_HRS');
    IF @KpiId IS NULL RETURN;

    INSERT INTO #KpiResult (KpiId, DepartmentId, ActualValue, Numerator, Denominator)
    SELECT @KpiId, f.DepartmentId,
           SUM(f.DurationMinutes) / 60.0, SUM(f.DurationMinutes), 60.0
    FROM core.FactDowntime f
    WHERE f.MonthKey = @MonthKey
    GROUP BY f.DepartmentId;

    INSERT INTO #KpiResult (KpiId, DepartmentId, ActualValue, Numerator, Denominator)
    SELECT @KpiId, -99,
           SUM(f.DurationMinutes) / 60.0, SUM(f.DurationMinutes), 60.0
    FROM core.FactDowntime f
    WHERE f.MonthKey = @MonthKey
    HAVING COUNT(*) > 0;
END
GO


/* =============================================================
   KPI 4 : COST_PER_UNIT  -  ต้นทุนต่อหน่วย (บาท)
   สูตร : SUM(Cost Amount) / SUM(QtyGood)

   หมายเหตุ: หารด้วยของดี (QtyGood) ไม่ใช่ยอดผลิตทั้งหมด
   เพราะของเสียขายไม่ได้ ต้นทุนต้องเฉลี่ยลงบนของที่ขายได้จริง
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_CalcKpi_CostPerUnit
    @MonthKey INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @KpiId INT = (SELECT KpiId FROM meta.KpiDefinition WHERE KpiCode = 'COST_PER_UNIT');
    IF @KpiId IS NULL RETURN;

    ;WITH cost AS (
        SELECT DepartmentId, SUM(Amount) AS TotalCost
        FROM core.FactCost WHERE MonthKey = @MonthKey
        GROUP BY DepartmentId
    ),
    good AS (
        SELECT DepartmentId, SUM(QtyGood) AS TotalGood
        FROM core.FactProduction WHERE MonthKey = @MonthKey
        GROUP BY DepartmentId
    )
    INSERT INTO #KpiResult (KpiId, DepartmentId, ActualValue, Numerator, Denominator)
    SELECT
        @KpiId,
        c.DepartmentId,
        c.TotalCost / NULLIF(g.TotalGood, 0),
        c.TotalCost,
        g.TotalGood
    FROM cost c
    JOIN good g ON g.DepartmentId = c.DepartmentId
    WHERE g.TotalGood > 0;          -- แผนก support ไม่มีผลผลิต ไม่มี cost/unit

    -- ระดับรวม
    INSERT INTO #KpiResult (KpiId, DepartmentId, ActualValue, Numerator, Denominator)
    SELECT
        @KpiId, -99,
        c.TotalCost / NULLIF(g.TotalGood, 0),
        c.TotalCost,
        g.TotalGood
    FROM (SELECT SUM(Amount)   AS TotalCost FROM core.FactCost       WHERE MonthKey = @MonthKey) c
    CROSS JOIN
         (SELECT SUM(QtyGood)  AS TotalGood FROM core.FactProduction WHERE MonthKey = @MonthKey) g
    /* SUM() แบบไม่มี GROUP BY จะคืน 1 แถวค่า NULL เมื่อไม่มีข้อมูล
       ไม่ใช่ 0 แถว จึงต้องกัน NULL เอง ไม่งั้นจะได้แถวขยะ */
    WHERE g.TotalGood > 0
      AND c.TotalCost IS NOT NULL;
END
GO


/* =============================================================
   KPI 5 : COST_DOWN_PCT  -  ผลการลดต้นทุน (%)
   สูตร : (Baseline - Actual) / Baseline * 100
   อ้างอิง COST_PER_UNIT ที่คำนวณไปแล้ว เทียบกับ baseline ใน meta.KpiTarget

   *** ต้องเรียกหลัง usp_CalcKpi_CostPerUnit เสมอ ***
   จึงกำหนด SortOrder ใน KpiDefinition ให้ COST_DOWN_PCT = 50 (ท้ายสุด)
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_CalcKpi_CostDown
    @MonthKey INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @KpiId    INT = (SELECT KpiId FROM meta.KpiDefinition WHERE KpiCode = 'COST_DOWN_PCT');
    DECLARE @CpuKpiId INT = (SELECT KpiId FROM meta.KpiDefinition WHERE KpiCode = 'COST_PER_UNIT');
    IF @KpiId IS NULL OR @CpuKpiId IS NULL RETURN;

    INSERT INTO #KpiResult (KpiId, DepartmentId, ActualValue, Numerator, Denominator)
    SELECT
        @KpiId,
        r.DepartmentId,
        (b.BaselineValue - r.ActualValue) * 100.0 / NULLIF(b.BaselineValue, 0),
        b.BaselineValue - r.ActualValue,
        b.BaselineValue
    FROM #KpiResult r
    CROSS APPLY (
        -- หา baseline: เอาของแผนกนั้นก่อน ถ้าไม่มีใช้ของระดับทุกแผนก
        SELECT TOP 1 t.BaselineValue
        FROM meta.KpiTarget t
        WHERE t.KpiId = @CpuKpiId
          AND t.MonthKey = @MonthKey
          AND (t.DepartmentId = r.DepartmentId OR t.DepartmentId IS NULL)
          AND t.BaselineValue IS NOT NULL
        ORDER BY CASE WHEN t.DepartmentId IS NULL THEN 1 ELSE 0 END
    ) b
    WHERE r.KpiId = @CpuKpiId
      AND r.ActualValue IS NOT NULL;
END
GO
