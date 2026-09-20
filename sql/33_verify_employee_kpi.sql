/* =============================================================
   33_verify_employee_kpi.sql
   Purpose : ตรวจว่าโมเดล KPI รายบุคคลทำงานถูกต้อง — อ่านอย่างเดียว
   ไม่ใช่ส่วนหนึ่งของการติดตั้ง รันหลังโหลด feed รอบแรก

   ทุกเช็คคืนคอลัมน์ Result = PASS / FAIL
   FAIL ทุกข้อมีคำอธิบายว่าหมายถึงอะไรและต้องไปดูที่ไหนต่อ
   ============================================================= */

USE KpiMonthlyReport;
GO

PRINT '=== 1) โครงสร้างพื้นฐาน ===';

SELECT 'ตารางครบ' AS Check_,
       CASE WHEN OBJECT_ID('core.FactKpiEmployeeMonthly') IS NOT NULL
             AND OBJECT_ID('stg.KpiEmployeeFeedRaw')      IS NOT NULL
             AND OBJECT_ID('meta.ReportSubscriptionDepartment') IS NOT NULL
            THEN 'PASS' ELSE 'FAIL — ยังรัน 30/32 ไม่ครบ' END AS Result;

SELECT 'นิยาม KPI รายบุคคล' AS Check_,
       CASE WHEN COUNT(*) >= 6 THEN 'PASS'
            ELSE 'FAIL — ต้องมี KPI Scope=EMPLOYEE อย่างน้อย 6 ตัว (รัน 30)' END AS Result,
       COUNT(*) AS ActiveEmployeeKpi
FROM meta.KpiDefinition WHERE Scope = 'EMPLOYEE' AND IsActive = 1;

SELECT 'ทะเบียนองค์กร' AS Check_,
       CASE WHEN d.Cnt >= 10 AND e.Cnt >= 100 THEN 'PASS'
            ELSE 'FAIL — ต้องมี 10 แผนก และพนักงาน 100 คน (รัน 31)' END AS Result,
       d.Cnt AS Departments, e.Cnt AS Employees
FROM (SELECT COUNT(*) Cnt FROM core.DimDepartment WHERE IsActive = 1 AND DepartmentId > 0) d
CROSS JOIN (SELECT COUNT(*) Cnt FROM core.DimEmployee WHERE IsActive = 1 AND EmployeeId > 0) e;

PRINT '=== 2) ข้อมูลที่โหลดเข้ามา ===';

SELECT 'มีข้อมูลรายบุคคล' AS Check_,
       CASE WHEN COUNT(*) > 0 THEN 'PASS'
            ELSE 'FAIL — ยังไม่ได้รัน KpiReport.Etl.exe kpi-feed' END AS Result,
       COUNT(*) AS FactRows,
       COUNT(DISTINCT MonthKey) AS Months
FROM core.FactKpiEmployeeMonthly;

/* ทุกคนในเดือนที่มีข้อมูล ควรมี KPI ครบทุกตัวที่เปิดใช้อยู่
   ขาดไปแปลว่า feed ส่งมาไม่ครบ หรือบางแถวถูก reject */
SELECT 'ทุกคนมี KPI ครบทุกตัว' AS Check_,
       CASE WHEN COUNT(*) = 0 THEN 'PASS'
            ELSE 'FAIL — มีพนักงานที่ feed ส่ง KPI มาไม่ครบ ดู meta.DataRejectLog' END AS Result,
       COUNT(*) AS IncompleteEmployeeMonths
FROM (
    SELECT f.MonthKey, f.EmployeeId
    FROM core.FactKpiEmployeeMonthly f
    GROUP BY f.MonthKey, f.EmployeeId
    HAVING COUNT(DISTINCT f.KpiId) <
           (SELECT COUNT(*) FROM meta.KpiDefinition WHERE Scope = 'EMPLOYEE' AND IsActive = 1)
) x;

/* แผนกในตาราง fact ต้องตรงกับแผนกในทะเบียนพนักงานเสมอ
   ไม่ตรง = transform เอาแผนกจาก feed มาใช้แทนทะเบียน ซึ่งไม่ควรเกิด */
SELECT 'แผนกตรงกับทะเบียนพนักงาน' AS Check_,
       CASE WHEN COUNT(*) = 0 THEN 'PASS'
            ELSE 'FAIL — มีแถวที่แผนกไม่ตรงกับ core.DimEmployee' END AS Result,
       COUNT(*) AS MismatchRows
FROM core.FactKpiEmployeeMonthly f
JOIN core.DimEmployee e ON e.EmployeeId = f.EmployeeId
WHERE e.DepartmentId <> f.DepartmentId;

PRINT '=== 3) การสรุปขึ้นระดับแผนก ===';

/* ทุกเดือน/แผนกที่มีข้อมูลรายบุคคล ต้องมีแถวสรุประดับแผนกด้วย
   ขาดไปแปลว่ายังไม่ได้รัน rollup หลังโหลดข้อมูล */
SELECT 'rollup ครบทุกเดือน/แผนก' AS Check_,
       CASE WHEN COUNT(*) = 0 THEN 'PASS'
            ELSE 'FAIL — รัน KpiReport.Etl.exe rollup' END AS Result,
       COUNT(*) AS MissingRollups
FROM (
    SELECT DISTINCT f.MonthKey, f.DepartmentId
    FROM core.FactKpiEmployeeMonthly f
) src
WHERE NOT EXISTS (
    SELECT 1 FROM core.FactKpiMonthly m
    WHERE m.MonthKey = src.MonthKey AND m.DepartmentId = src.DepartmentId
);

/* ตัวเลขสองระดับต้องตรงกัน: % ความสำเร็จของแผนกที่ rollup เก็บไว้
   ต้องเท่ากับที่คำนวณสดจากข้อมูลรายบุคคล (ยอมคลาดได้ 0.05 จากการปัดเศษ) */
SELECT 'ค่าระดับแผนกตรงกับรายบุคคล' AS Check_,
       CASE WHEN COUNT(*) = 0 THEN 'PASS'
            ELSE 'FAIL — ค่าไม่ตรง แปลว่ามีใครเขียน core.FactKpiMonthly นอกเส้นทาง rollup' END AS Result,
       COUNT(*) AS DriftedRows
FROM core.FactKpiMonthly m
JOIN meta.KpiDefinition k ON k.KpiId = m.KpiId AND k.KpiCode = 'DEPT_KPI_COMPLETION'
JOIN (
    SELECT MonthKey, DepartmentId,
           100.0 * SUM(CAST(IsComplete AS DECIMAL(18,4))) / NULLIF(COUNT(*), 0) AS Pct
    FROM core.FactKpiEmployeeMonthly
    GROUP BY MonthKey, DepartmentId
) live ON live.MonthKey = m.MonthKey AND live.DepartmentId = m.DepartmentId
WHERE ABS(m.ActualValue - live.Pct) > 0.05;

PRINT '=== 4) ภาพรวมที่ควรเห็นบนหน้า Monitoring ===';

SELECT TOP (20) *
FROM rpt.vw_DepartmentKpiCompletion
ORDER BY MonthKey DESC, KpiCompletionPct ASC;

PRINT '=== 5) แถวที่ระบบปฏิเสธในรอบล่าสุด ===';

SELECT TOP (20) RejectReason, COUNT(*) AS Rows_
FROM meta.DataRejectLog
WHERE SourceTable = 'stg.KpiEmployeeFeedRaw'
  AND RunId = (SELECT MAX(RunId) FROM meta.DataRejectLog WHERE SourceTable = 'stg.KpiEmployeeFeedRaw')
GROUP BY RejectReason
ORDER BY Rows_ DESC;
GO
