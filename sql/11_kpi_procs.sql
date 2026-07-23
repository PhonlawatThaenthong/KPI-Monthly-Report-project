/* =============================================================
   11_kpi_procs.sql
   Purpose : Stored Procedure คำนวณ KPI ทั้ง 5 ตัว + ตัวสั่งงาน
   Idempotent : YES (คำนวณเดือนเดิมซ้ำ ทับค่าเดิม ไม่เพิ่มแถว)

   สถาปัตยกรรม
     usp_RunKpi_Monthly (orchestrator)
       1. สร้าง #KpiResult
       2. เรียก calc proc ทีละตัวตาม meta.KpiDefinition.CalcProcName
          -> แต่ละตัว INSERT ผลดิบลง #KpiResult
       3. เติม Target / PrevMonth / StatusFlag ให้ทีเดียว
       4. MERGE เข้า core.FactKpiMonthly

   ทำไมแยกแบบนี้: logic เติมเป้าหมายและตัดสินสีเหมือนกันทุก KPI
   เขียนที่เดียวพอ calc proc แต่ละตัวจะได้สั้นและอ่านง่าย
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