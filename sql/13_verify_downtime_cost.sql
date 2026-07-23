/* =============================================================
   13_verify_downtime_cost.sql
   Purpose : ตรวจผลหลังรัน KpiReport.Etl.exe downtime / cost / kpi-all
   ============================================================= */

USE KpiMonthlyReport;
GO

/* ---------- 1) ประวัติการรันล่าสุด 10 รอบ ---------- */
SELECT TOP 10
    RunId, JobName, Status, RowsRead, RowsWritten, RowsRejected,
    DurationSec, StartedAt, ErrorMessage
FROM meta.EtlRunLog
ORDER BY RunId DESC;
GO

/* ---------- 2) เหตุผล reject ของ Downtime ---------- */
SELECT RejectReason, COUNT(*) AS Cnt
FROM meta.DataRejectLog
WHERE SourceTable = 'stg.DowntimeRaw'
GROUP BY RejectReason
ORDER BY Cnt DESC;
GO

/* ---------- 3) เหตุผล reject ของ Cost ----------
   คาดว่า PENDING_AMOUNT ~3 แถว (ตรงกับ NULL_AMOUNT ใน manifest)      */
SELECT RejectReason, COUNT(*) AS Cnt
FROM meta.DataRejectLog
WHERE SourceTable = 'stg.CostRaw'
GROUP BY RejectReason
ORDER BY Cnt DESC;
GO

/* ---------- 4) ข้อมูล Downtime กระจายตามเดือน ---------- */
SELECT
    MonthKey,
    COUNT(*)                       AS EventCount,
    SUM(DurationMinutes)           AS TotalMinutes,
    CAST(SUM(DurationMinutes)/60.0 AS DECIMAL(10,1)) AS TotalHours
FROM core.FactDowntime
GROUP BY MonthKey
ORDER BY MonthKey;
GO

/* ---------- 5) ข้อมูล Cost กระจายตามเดือน x ประเภท ---------- */
SELECT
    c.MonthKey, t.CostTypeCode, SUM(c.Amount) AS TotalAmount
FROM core.FactCost c
JOIN core.DimCostType t ON t.CostTypeId = c.CostTypeId
GROUP BY c.MonthKey, t.CostTypeCode
ORDER BY c.MonthKey, t.CostTypeCode;
GO

/* ---------- 6) ยอดต้นทุนรวมรายเดือน (เทียบกับที่เคย query ตอนสำรวจ Excel) ---------- */
SELECT MonthKey, SUM(Amount) AS TotalCost
FROM core.FactCost
GROUP BY MonthKey
ORDER BY MonthKey;
GO

/* ---------- 7) ตรวจว่า KPI ครบทั้ง 5 ตัวแล้ว ---------- */
SELECT KpiCode, COUNT(DISTINCT MonthKey) AS MonthsWithData
FROM rpt.vw_KpiMonthly
WHERE DepartmentId = -99
GROUP BY KpiCode
ORDER BY KpiCode;
GO

/* ---------- 8) ดู Cost per Unit + Cost Down ระดับรวม ---------- */
SELECT MonthKey, MonthLabel, KpiCode, ActualValue, TargetValue, StatusFlag
FROM rpt.vw_KpiMonthly
WHERE DepartmentId = -99
  AND KpiCode IN ('COST_PER_UNIT', 'COST_DOWN_PCT', 'DOWNTIME_HRS')
ORDER BY MonthKey, KpiCode;
GO

/* =============================================================
   บททดสอบ IDEMPOTENCY
   รันชุดนี้ -> รัน KpiReport.Etl.exe run-all ซ้ำ -> รันชุดนี้อีกครั้ง
   ตัวเลขต้องเท่าเดิมเป๊ะ (ไฟล์จะถูกข้ามเพราะ hash ซ้ำ)
   ============================================================= */
SELECT
    (SELECT COUNT(*) FROM core.FactDowntime)                AS DowntimeRows,
    (SELECT SUM(DurationMinutes) FROM core.FactDowntime)     AS DowntimeMinutes,
    (SELECT COUNT(*) FROM core.FactCost)                     AS CostRows,
    (SELECT SUM(Amount) FROM core.FactCost)                  AS CostTotal;
GO
