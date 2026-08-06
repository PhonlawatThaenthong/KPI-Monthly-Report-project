/* =============================================================
   20_valid_month_hr.sql
   Purpose : เปลี่ยนเกณฑ์ "เดือนที่มีข้อมูลจริง" ให้อิง attendance
             แทน production (เพราะ KPI การผลิตถูกปิด IsActive=0 แล้ว)
   Idempotent : YES

   หลักการ: เดือนจริงมีพนักงานลงเวลาครบ ~40 คน
            เดือนผีมีข้อมูลหลุดมาแค่ไม่กี่คน จะตกเกณฑ์
   ============================================================= */

USE KpiMonthlyReport;
GO

CREATE OR ALTER VIEW rpt.vw_ValidMonth
AS
WITH emp_per_month AS (
    -- นับจำนวนพนักงานที่มีข้อมูลลงเวลาในแต่ละเดือน
    SELECT MonthKey, COUNT(DISTINCT EmployeeId) AS EmployeeCount
    FROM core.FactAttendance
    GROUP BY MonthKey
),
threshold AS (
    -- เกณฑ์: อย่างน้อย 50% ของจำนวนพนักงานเฉลี่ยต่อเดือน
    SELECT 0.50 * AVG(CAST(EmployeeCount AS FLOAT)) AS MinEmployees
    FROM emp_per_month
)
SELECT e.MonthKey
FROM emp_per_month e
CROSS JOIN threshold t
WHERE e.EmployeeCount >= t.MinEmployees;
GO

/* ตรวจผล: เดือนไหน valid เดือนไหนผี */
SELECT
    e.MonthKey,
    e.Cnt AS EmployeeCount,
    CASE WHEN vm.MonthKey IS NULL THEN 'GHOST - filtered' ELSE 'valid' END AS Status
FROM (
    SELECT MonthKey, COUNT(DISTINCT EmployeeId) AS Cnt
    FROM core.FactAttendance GROUP BY MonthKey
) e
LEFT JOIN rpt.vw_ValidMonth vm ON vm.MonthKey = e.MonthKey
ORDER BY e.MonthKey;
GO
