/* =============================================================
   22_report_subscription.sql
   Purpose : รายชื่อผู้รับรายงาน KPI รายเดือนทางอีเมล
             แยกขอบเขตข้อมูลได้ต่อคน ตามหลักเดียวกับสิทธิ์ในเว็บ
   Idempotent : YES

   หลักการ
   ---------------------------------------------------------------
   DepartmentId = NULL  -> ได้รายงานภาพรวมทั้งบริษัท + แยกรายแผนก
                           (เทียบเท่า role Admin/Manager ในเว็บ)
   DepartmentId = ตัวเลข -> ได้เฉพาะแผนกนั้นแผนกเดียว
                           (เทียบเท่า Viewer ที่ผูกกับแผนก)

   จงใจแยกตารางนี้ออกจาก AspNetUsers เพราะผู้รับรายงานไม่จำเป็น
   ต้องเป็นคนที่มีบัญชีในเว็บ เช่นผู้บริหารที่อยากได้แค่ไฟล์ทางอีเมล
   ============================================================= */

USE KpiMonthlyReport;
GO

IF OBJECT_ID('meta.ReportSubscription') IS NULL
BEGIN
CREATE TABLE meta.ReportSubscription
(
    SubscriptionId  INT             IDENTITY(1,1) NOT NULL,
    Email           NVARCHAR(256)   NOT NULL,
    DisplayName     NVARCHAR(200)   NULL,

    /* NULL = เห็นทุกแผนก */
    DepartmentId    INT             NULL,

    IsActive        BIT             NOT NULL CONSTRAINT DF_ReportSub_Active  DEFAULT (1),
    CreatedAt       DATETIME2(0)    NOT NULL CONSTRAINT DF_ReportSub_Created DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_ReportSubscription PRIMARY KEY CLUSTERED (SubscriptionId)
);

/* กันสมัครซ้ำในขอบเขตเดียวกัน
   ใช้ filtered unique index สองตัวเพราะ NULL ใน SQL Server
   ไม่เท่ากับ NULL การใส่ UNIQUE(Email, DepartmentId) เฉย ๆ
   จะยอมให้แถว (a@x.com, NULL) ซ้ำได้ไม่จำกัด */
CREATE UNIQUE INDEX UX_ReportSub_Email_Dept
    ON meta.ReportSubscription(Email, DepartmentId)
    WHERE DepartmentId IS NOT NULL;

CREATE UNIQUE INDEX UX_ReportSub_Email_AllDept
    ON meta.ReportSubscription(Email)
    WHERE DepartmentId IS NULL;

PRINT '>> Created meta.ReportSubscription';
END
ELSE
    PRINT '>> meta.ReportSubscription already exists';
GO

/* -------------------------------------------------------------
   view ที่ job อ่าน — เติมชื่อแผนกมาให้เลย
   job จะได้ไม่ต้อง join เอง และไม่ต้องรู้จัก core.DimDepartment
   ------------------------------------------------------------- */
CREATE OR ALTER VIEW meta.vw_ActiveReportSubscription
AS
SELECT  s.SubscriptionId,
        s.Email,
        s.DisplayName,
        s.DepartmentId,
        d.DepartmentName
FROM        meta.ReportSubscription s
LEFT JOIN   core.DimDepartment      d ON d.DepartmentId = s.DepartmentId
WHERE       s.IsActive = 1;
GO

/* -------------------------------------------------------------
   ตัวอย่างผู้รับ — แก้/ลบได้ตามจริง
   ใส่เฉพาะเมื่อตารางยังว่าง จะได้รันสคริปต์ซ้ำได้โดยไม่ทับของจริง
   ------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM meta.ReportSubscription)
BEGIN
    INSERT INTO meta.ReportSubscription (Email, DisplayName, DepartmentId)
    VALUES (N'hr-analytics@example.com', N'HR Analytics', NULL);

    INSERT INTO meta.ReportSubscription (Email, DisplayName, DepartmentId)
    SELECT N'linea-supervisor@example.com', N'Line A Supervisor', DepartmentId
    FROM core.DimDepartment WHERE DepartmentCode = 'LINE_A';

    PRINT '>> Seeded example subscriptions (แก้เป็นอีเมลจริงก่อนใช้งาน)';
END
GO

/* -------------------------------------------------------------
   สิทธิ์ของ role ที่ ETL ใช้

   งานส่งรายงานรันในโปรเซสเดียวกับ ETL แต่ต้องอ่านข้อมูลผ่าน schema rpt
   ตัวเดียวกับที่เว็บใช้ (rpt.usp_GetKpiDashboard, rpt.vw_ValidMonth)
   เพื่อให้ตัวเลขในอีเมลตรงกับหน้าจอเป๊ะ ๆ

   01_database_and_schemas.sql ให้ db_kpi_etl แค่ stg/core/meta
   ยังไม่ได้ให้ rpt จึงต้องเพิ่มตรงนี้
   ------------------------------------------------------------- */
IF DATABASE_PRINCIPAL_ID('db_kpi_etl') IS NOT NULL
BEGIN
    GRANT SELECT  ON meta.vw_ActiveReportSubscription TO db_kpi_etl;
    GRANT SELECT  ON SCHEMA::rpt TO db_kpi_etl;
    GRANT EXECUTE ON SCHEMA::rpt TO db_kpi_etl;
    PRINT '>> Granted rpt SELECT/EXECUTE + subscription view to db_kpi_etl';
END
GO
