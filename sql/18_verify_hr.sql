/* =============================================================
   18_verify_hr.sql
   Purpose : ตรวจ KPI บุคลากรหลังรัน attendance + kpi-all
   ============================================================= */

USE KpiMonthlyReport;
GO

/* 1) การรัน ETL ล่าสุด */
SELECT TOP 5 RunId, JobName, Status, RowsRead, RowsWritten, RowsRejected, StartedAt
FROM meta.EtlRunLog WHERE JobName = 'ETL_Attendance'
ORDER BY RunId DESC;
GO

/* 2) reject ของ attendance (คาด NEGATIVE_OT ~34) */
SELECT RejectReason, COUNT(*) AS Cnt
FROM meta.DataRejectLog WHERE SourceTable = 'stg.AttendanceRaw'
GROUP BY RejectReason ORDER BY Cnt DESC;
GO

/* 3) FactAttendance กระจายตามเดือน */
SELECT MonthKey, COUNT(*) AS Rows,
       COUNT(DISTINCT EmployeeId) AS Employees,
       SUM(OtHours) AS TotalOt
FROM core.FactAttendance
GROUP BY MonthKey ORDER BY MonthKey;
GO

/* 4) KPI บุคลากรระดับรวม */
SELECT MonthKey, MonthLabel, KpiCode, ActualValue, TargetValue, StatusFlag
FROM rpt.vw_KpiMonthly
WHERE DepartmentId = -99 AND CategoryName = 'HR'
ORDER BY MonthKey, KpiCode;
GO

/* 5) KPI บุคลากรแยกแผนก เดือนล่าสุด */
SELECT DepartmentName, KpiCode, ActualValue, TargetValue, StatusFlag
FROM rpt.vw_KpiMonthly
WHERE CategoryName = 'HR'
  AND MonthKey = (SELECT MAX(MonthKey) FROM core.FactAttendance)
  AND DepartmentId > 0
ORDER BY DepartmentName, KpiCode;
GO

/* 6) เช็ค KPI ครบทั้ง 8 ตัวแล้ว (5 production + 3 HR) */
SELECT CategoryName, KpiCode, COUNT(DISTINCT MonthKey) AS Months
FROM rpt.vw_KpiMonthly WHERE DepartmentId = -99
GROUP BY CategoryName, KpiCode
ORDER BY CategoryName, KpiCode;
GO
