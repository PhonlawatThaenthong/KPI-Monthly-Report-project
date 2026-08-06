/* =============================================================
   19_fix_ghost_months.sql
   Purpose : กันเดือนผี (จากวันที่กำกวม) ออกจากกราฟ trend
             แก้ที่ rpt.usp_GetKpiTrend ตัวเดียว
   Idempotent : YES
   ============================================================= */

USE KpiMonthlyReport;
GO

/* -------------------------------------------------------------
   rpt.vw_ValidMonth
   นิยาม "เดือนที่มีข้อมูลจริงเพียงพอ" ไว้ที่เดียว ใช้ซ้ำได้
   เกณฑ์: ผลผลิตรวมของเดือนนั้น >= 20% ของค่าเฉลี่ย
   เดือนผีที่มีข้อมูลหลุดมาไม่กี่แถวจะตกเกณฑ์นี้
   ------------------------------------------------------------- */
CREATE OR ALTER VIEW rpt.vw_ValidMonth
AS
WITH prod AS (
    SELECT MonthKey, ActualValue
    FROM rpt.vw_KpiMonthly
    WHERE DepartmentId = -99 AND KpiCode = 'PROD_OUTPUT' AND ActualValue IS NOT NULL
),
threshold AS (
    SELECT 0.20 * AVG(ActualValue) AS MinOutput FROM prod
)
SELECT p.MonthKey
FROM prod p
CROSS JOIN threshold t
WHERE p.ActualValue >= t.MinOutput;
GO

/* -------------------------------------------------------------
   แก้ usp_GetKpiTrend ให้ดึงเฉพาะเดือนที่ผ่านเกณฑ์
   ------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE rpt.usp_GetKpiTrend
    @KpiCode        VARCHAR(30),
    @DepartmentId   INT = -99,
    @MonthsBack     INT = 12
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH ranked AS (
        SELECT
            t.MonthKey, t.MonthLabel, t.ActualValue, t.TargetValue, t.Unit, t.Direction,
            ROW_NUMBER() OVER (ORDER BY t.MonthKey DESC) AS rn
        FROM rpt.vw_KpiMonthly t
        WHERE t.KpiCode = @KpiCode
          AND t.DepartmentId = @DepartmentId
          -- เฉพาะเดือนที่มีข้อมูลจริงเพียงพอ (กันเดือนผี)
          AND t.MonthKey IN (SELECT MonthKey FROM rpt.vw_ValidMonth)
    )
    SELECT MonthKey, MonthLabel, ActualValue, TargetValue, Unit, Direction
    FROM ranked
    WHERE rn <= @MonthsBack
    ORDER BY MonthKey;
END
GO

/* -------------------------------------------------------------
   ตรวจผล: เทียบเดือนทั้งหมด vs เดือนที่ผ่านเกณฑ์
   ------------------------------------------------------------- */
SELECT 'All months' AS Scope, COUNT(DISTINCT MonthKey) AS Cnt
FROM rpt.vw_KpiMonthly WHERE DepartmentId = -99
UNION ALL
SELECT 'Valid months', COUNT(*) FROM rpt.vw_ValidMonth;
GO

-- ดูว่าเดือนไหนถูกกรองออก (เดือนผี)
SELECT DISTINCT v.MonthKey,
    CASE WHEN vm.MonthKey IS NULL THEN 'GHOST - filtered out' ELSE 'valid' END AS Status
FROM rpt.vw_KpiMonthly v
LEFT JOIN rpt.vw_ValidMonth vm ON vm.MonthKey = v.MonthKey
WHERE v.DepartmentId = -99
ORDER BY v.MonthKey;
GO
