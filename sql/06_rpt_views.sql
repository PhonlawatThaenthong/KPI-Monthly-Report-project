/* =============================================================
   06_rpt_views.sql
   Purpose : View ชั้น rpt สำหรับ Dashboard / Report
             Web อ่านจากชั้นนี้เท่านั้น ห้าม query core ตรง ๆ
   Idempotent : YES
   ============================================================= */

USE KpiMonthlyReport;
GO

/* -------------------------------------------------------------
   rpt.vw_KpiMonthly  -  ตารางหลักของ Dashboard
   ------------------------------------------------------------- */
CREATE OR ALTER VIEW rpt.vw_KpiMonthly
AS
SELECT
    f.MonthKey,
    LEFT(CONVERT(VARCHAR(6), f.MonthKey), 4) + '-' +
        RIGHT(CONVERT(VARCHAR(6), f.MonthKey), 2)   AS MonthLabel,
    f.KpiId,
    k.KpiCode,
    k.KpiName,
    k.KpiNameTh,
    k.CategoryName,
    k.Unit,
    k.DecimalPlaces,
    k.Direction,
    k.FormulaText,
    k.SortOrder,
    f.DepartmentId,
    d.DepartmentCode,
    d.DepartmentName,
    f.ActualValue,
    f.TargetValue,
    f.BaselineValue,
    f.PrevMonthValue,
    f.Variance,
    f.AchievementPct,
    f.MoMChangePct,
    f.StatusFlag,
    f.CalculatedAt
FROM core.FactKpiMonthly f
JOIN meta.KpiDefinition  k ON k.KpiId = f.KpiId
JOIN core.DimDepartment  d ON d.DepartmentId = f.DepartmentId
WHERE k.IsActive = 1;
GO

/* -------------------------------------------------------------
   rpt.vw_KpiTrend  -  ย้อนหลัง 12 เดือน สำหรับกราฟเส้น
   ------------------------------------------------------------- */
CREATE OR ALTER VIEW rpt.vw_KpiTrend
AS
SELECT TOP (100) PERCENT
    v.KpiId, v.KpiCode, v.KpiName, v.Unit, v.Direction,
    v.DepartmentId, v.DepartmentName,
    v.MonthKey, v.MonthLabel,
    v.ActualValue, v.TargetValue,
    ROW_NUMBER() OVER (PARTITION BY v.KpiId, v.DepartmentId
                       ORDER BY v.MonthKey DESC) AS MonthRank
FROM rpt.vw_KpiMonthly v
ORDER BY v.KpiId, v.DepartmentId, v.MonthKey;
GO

/* -------------------------------------------------------------
   rpt.vw_ProductionSummary  -  สรุปการผลิตรายเดือน x แผนก
   ------------------------------------------------------------- */
CREATE OR ALTER VIEW rpt.vw_ProductionSummary
AS
SELECT
    f.MonthKey,
    f.DepartmentId,
    d.DepartmentName,
    SUM(f.QtyProduced)              AS TotalProduced,
    SUM(f.QtyDefect)                AS TotalDefect,
    SUM(f.QtyGood)                  AS TotalGood,
    SUM(f.RunHours)                 AS TotalRunHours,
    CASE WHEN SUM(f.QtyProduced) = 0 THEN NULL
         ELSE SUM(f.QtyDefect) * 100.0 / NULLIF(SUM(f.QtyProduced), 0) END AS DefectRatePct,
    COUNT(DISTINCT f.DateKey)       AS WorkingDays
FROM core.FactProduction f
JOIN core.DimDepartment d ON d.DepartmentId = f.DepartmentId
GROUP BY f.MonthKey, f.DepartmentId, d.DepartmentName;
GO

/* -------------------------------------------------------------
   rpt.vw_CostSummary  -  ต้นทุนรายเดือน แยกประเภท
   ------------------------------------------------------------- */
CREATE OR ALTER VIEW rpt.vw_CostSummary
AS
SELECT
    c.MonthKey,
    c.DepartmentId,
    d.DepartmentName,
    t.CostTypeCode,
    t.CostTypeName,
    SUM(c.Amount) AS TotalAmount
FROM core.FactCost c
JOIN core.DimDepartment d ON d.DepartmentId = c.DepartmentId
JOIN core.DimCostType   t ON t.CostTypeId  = c.CostTypeId
GROUP BY c.MonthKey, c.DepartmentId, d.DepartmentName, t.CostTypeCode, t.CostTypeName;
GO

/* -------------------------------------------------------------
   rpt.vw_CostDown  -  ติดตามผลการลดต้นทุนเทียบ baseline
   ------------------------------------------------------------- */
CREATE OR ALTER VIEW rpt.vw_CostDown
AS
SELECT
    v.MonthKey,
    v.MonthLabel,
    v.DepartmentId,
    v.DepartmentName,
    v.ActualValue                       AS CostPerUnitActual,
    v.BaselineValue                     AS CostPerUnitBaseline,
    v.BaselineValue - v.ActualValue     AS SavingPerUnit,
    CASE WHEN v.BaselineValue IS NULL OR v.BaselineValue = 0 THEN NULL
         ELSE (v.BaselineValue - v.ActualValue) * 100.0 / NULLIF(v.BaselineValue, 0)
    END                                 AS CostDownPct,
    v.TargetValue                       AS CostDownTargetPct,
    v.StatusFlag
FROM rpt.vw_KpiMonthly v
WHERE v.KpiCode = 'COST_PER_UNIT';
GO

/* -------------------------------------------------------------
   rpt.vw_EtlHealth  -  หน้าจอ Admin ดูสถานะการรัน ETL
   ------------------------------------------------------------- */
CREATE OR ALTER VIEW rpt.vw_EtlHealth
AS
SELECT TOP (200)
    r.RunId,
    r.JobName,
    r.MonthKey,
    r.StartedAt,
    r.FinishedAt,
    r.DurationSec,
    r.Status,
    r.RowsRead,
    r.RowsWritten,
    r.RowsRejected,
    r.TriggeredBy,
    r.ErrorMessage,
    (SELECT COUNT(*) FROM meta.EtlRunStep s WHERE s.RunId = r.RunId) AS StepCount,
    (SELECT COUNT(*) FROM meta.DataRejectLog j WHERE j.RunId = r.RunId) AS RejectCount
FROM meta.EtlRunLog r
ORDER BY r.StartedAt DESC;
GO

/* -------------------------------------------------------------
   rpt.usp_GetKpiDashboard
   Web เรียกอันนี้ - รองรับ row-level filter ตาม role
   @DepartmentId NULL = เห็นทุกแผนก (Admin/Manager)
   ------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE rpt.usp_GetKpiDashboard
    @MonthKey       INT,
    @DepartmentId   INT = NULL,
    @CategoryName   NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM rpt.vw_KpiMonthly v
    WHERE v.MonthKey = @MonthKey
      AND (@DepartmentId IS NULL OR v.DepartmentId = @DepartmentId)
      AND (@CategoryName IS NULL OR v.CategoryName = @CategoryName)
    ORDER BY v.SortOrder, v.KpiCode, v.DepartmentId;
END
GO

/* -------------------------------------------------------------
   rpt.usp_GetKpiTrend  -  ข้อมูลกราฟ N เดือนล่าสุด
   ------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE rpt.usp_GetKpiTrend
    @KpiCode        VARCHAR(30),
    @DepartmentId   INT = -99,
    @MonthsBack     INT = 12
AS
BEGIN
    SET NOCOUNT ON;

    SELECT MonthKey, MonthLabel, ActualValue, TargetValue, Unit, Direction
    FROM rpt.vw_KpiTrend
    WHERE KpiCode = @KpiCode
      AND DepartmentId = @DepartmentId
      AND MonthRank <= @MonthsBack
    ORDER BY MonthKey;
END
GO
