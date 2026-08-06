/* =============================================================
   14_rpt_department_view.sql
   Purpose : เปิดให้เว็บอ่านรายชื่อแผนกผ่าน rpt แทนการ query core.DimDepartment ตรง ๆ
             (db_kpi_web มีสิทธิ์ SELECT เฉพาะ schema rpt เท่านั้น ดู 01_database_and_schemas.sql)
   Idempotent : YES
   ============================================================= */

USE KpiMonthlyReport;
GO

CREATE OR ALTER VIEW rpt.vw_Department
AS
SELECT DepartmentId, DepartmentCode, DepartmentName, DepartmentNameTh, IsActive
FROM core.DimDepartment
WHERE IsActive = 1;
GO
