/* =============================================================
   23_report_subscription_user_link.sql
   Purpose : ผูกผู้รับรายงานเข้ากับบัญชีผู้ใช้ในระบบได้ (ไม่บังคับ)
             และให้อีเมลภายนอกที่ไม่มีบัญชียังเพิ่มเองได้เหมือนเดิม
   Idempotent : YES
   ต้องรันหลัง 22_report_subscription.sql

   ปัญหาที่แก้
   ---------------------------------------------------------------
   เดิม meta.ReportSubscription เก็บอีเมลเป็นข้อความล้วน ไม่รู้จัก
   บัญชีในระบบ ทำให้เกิดรายการที่ต้องดูแลสองชุด:
   ปิดบัญชีพนักงานที่ลาออกในหน้า Users แล้ว เขายังได้รับรายงาน
   ทุกเดือนต่อไป จนกว่าจะมีคนนึกได้ว่าต้องมาลบตรงนี้ด้วย

   วิธีแก้
   ---------------------------------------------------------------
   เพิ่มคอลัมน์ UserId (NULL ได้)
     UserId มีค่า  -> ผูกกับบัญชีในระบบ อีเมลและชื่อดึงจากบัญชีนั้นสด ๆ
                      บัญชีถูกปิด = หยุดส่งอัตโนมัติ ไม่ต้อง sync อะไร
     UserId = NULL -> อีเมลภายนอกที่พิมพ์เอง เช่นผู้บริหารที่ไม่มีบัญชี

   จงใจ "คำนวณสด" จาก view แทนการตั้งธงเก็บไว้ เพราะธงที่ต้อง sync
   คือที่มาของบั๊กแบบเดียวกับที่กำลังแก้อยู่นี้
   ============================================================= */

USE KpiMonthlyReport;
GO

/* ---------- 1) คอลัมน์ UserId ---------- */
IF COL_LENGTH('meta.ReportSubscription', 'UserId') IS NULL
BEGIN
    ALTER TABLE meta.ReportSubscription
        ADD UserId NVARCHAR(128) NULL;
    PRINT '>> Added meta.ReportSubscription.UserId';
END
GO

/* ---------- 2) Email ยอมให้ว่างได้เมื่อผูกกับ user ---------- */
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('meta.ReportSubscription')
             AND name = 'Email' AND is_nullable = 0)
BEGIN
    ALTER TABLE meta.ReportSubscription ALTER COLUMN Email NVARCHAR(256) NULL;
    PRINT '>> meta.ReportSubscription.Email is now nullable (ผูก user แล้วไม่ต้องกรอก)';
END
GO

/* ต้องมีอย่างใดอย่างหนึ่งเสมอ: ผูก user หรือกรอกอีเมลเอง */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_ReportSub_UserOrEmail')
BEGIN
    ALTER TABLE meta.ReportSubscription
        ADD CONSTRAINT CK_ReportSub_UserOrEmail
        CHECK (UserId IS NOT NULL OR Email IS NOT NULL);
    PRINT '>> Added CK_ReportSub_UserOrEmail';
END
GO

/* ---------- 3) คีย์กันซ้ำ ----------
   ของเดิมกันซ้ำจากอีเมลอย่างเดียว พอมี UserId เข้ามาต้องกันทั้งสองแบบ
   ใช้คอลัมน์คำนวณตัวเดียวแทนการทำ index แยกสี่ตัว:
   ผูก user -> ใช้ UserId, ไม่ผูก -> ใช้อีเมลตัวพิมพ์เล็ก
   (a@x.com กับ A@X.com คือคนเดียวกัน)                              */
IF COL_LENGTH('meta.ReportSubscription', 'DedupeKey') IS NULL
BEGIN
    ALTER TABLE meta.ReportSubscription
        ADD DedupeKey AS (COALESCE(UserId, LOWER(Email))) PERSISTED;
    PRINT '>> Added computed column DedupeKey';
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ReportSub_Email_Dept'
           AND object_id = OBJECT_ID('meta.ReportSubscription'))
    DROP INDEX UX_ReportSub_Email_Dept ON meta.ReportSubscription;
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ReportSub_Email_AllDept'
           AND object_id = OBJECT_ID('meta.ReportSubscription'))
    DROP INDEX UX_ReportSub_Email_AllDept ON meta.ReportSubscription;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ReportSub_Key_Dept'
               AND object_id = OBJECT_ID('meta.ReportSubscription'))
    CREATE UNIQUE INDEX UX_ReportSub_Key_Dept
        ON meta.ReportSubscription(DedupeKey, DepartmentId)
        WHERE DepartmentId IS NOT NULL;
GO

/* NULL ไม่เท่ากับ NULL ใน SQL Server แถวขอบเขต "ทุกแผนก"
   จึงต้องแยก index ต่างหาก ไม่งั้นซ้ำได้ไม่จำกัด */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ReportSub_Key_AllDept'
               AND object_id = OBJECT_ID('meta.ReportSubscription'))
    CREATE UNIQUE INDEX UX_ReportSub_Key_AllDept
        ON meta.ReportSubscription(DedupeKey)
        WHERE DepartmentId IS NULL;
GO

/* ---------- 4) view ที่ job ใช้ส่งจริง ---------- */
CREATE OR ALTER VIEW meta.vw_ActiveReportSubscription
AS
SELECT  s.SubscriptionId,

        /* ผูก user แล้วให้ยึดอีเมลของบัญชีเป็นหลักเสมอ
           เปลี่ยนอีเมลในบัญชี รายงานก็ตามไปเอง ไม่ต้องแก้สองที่ */
        COALESCE(u.Email, u.UserName, s.Email)      AS Email,
        COALESCE(s.DisplayName, u.UserName, s.Email) AS DisplayName,

        s.DepartmentId,
        d.DepartmentName
FROM        meta.ReportSubscription s
LEFT JOIN   dbo.AspNetUsers         u ON u.Id = s.UserId
LEFT JOIN   core.DimDepartment      d ON d.DepartmentId = s.DepartmentId
WHERE   s.IsActive = 1
        /* บัญชีที่ถูกปิดใช้งานหยุดรับรายงานทันที โดยไม่ต้องมีใครมาปิดซ้ำตรงนี้
           อีเมลภายนอก (UserId IS NULL) ไม่มีเงื่อนไขนี้ */
        AND (
              s.UserId IS NULL
              OR (u.Id IS NOT NULL
                  AND (u.LockoutEndDateUtc IS NULL
                       OR u.LockoutEndDateUtc <= SYSUTCDATETIME()))
            );
GO

/* ---------- 5) view สำหรับหน้าจัดการในเว็บ ----------
   ต่างจากด้านบนตรงที่แสดง "ทุกแถว" รวมที่ถูกปิดอยู่ พร้อมบอกเหตุผล
   ผู้ดูแลจะได้เห็นว่าแถวไหนเงียบอยู่เพราะอะไร ไม่ใช่หายไปเฉย ๆ    */
CREATE OR ALTER VIEW meta.vw_ReportSubscriptionAdmin
AS
SELECT  s.SubscriptionId,
        s.UserId,
        COALESCE(u.Email, u.UserName, s.Email)       AS Email,
        COALESCE(s.DisplayName, u.UserName, s.Email) AS DisplayName,
        s.DepartmentId,
        d.DepartmentName,
        s.IsActive,
        s.CreatedAt,
        CAST(CASE WHEN s.UserId IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS IsLinkedToUser,
        CAST(CASE WHEN s.UserId IS NOT NULL AND u.Id IS NULL
                  THEN 1 ELSE 0 END AS BIT)                          AS LinkedUserMissing,
        CAST(CASE WHEN u.LockoutEndDateUtc IS NOT NULL
                       AND u.LockoutEndDateUtc > SYSUTCDATETIME()
                  THEN 1 ELSE 0 END AS BIT)                          AS LinkedUserDisabled
FROM        meta.ReportSubscription s
LEFT JOIN   dbo.AspNetUsers         u ON u.Id = s.UserId
LEFT JOIN   core.DimDepartment      d ON d.DepartmentId = s.DepartmentId;
GO

/* ---------- 6) สิทธิ์ ---------- */
IF DATABASE_PRINCIPAL_ID('db_kpi_web') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, UPDATE, DELETE ON meta.ReportSubscription        TO db_kpi_web;
    GRANT SELECT                          ON meta.vw_ReportSubscriptionAdmin TO db_kpi_web;
    PRINT '>> Granted meta.ReportSubscription CRUD to db_kpi_web';
END
GO

IF DATABASE_PRINCIPAL_ID('db_kpi_etl') IS NOT NULL
BEGIN
    GRANT SELECT ON meta.vw_ActiveReportSubscription TO db_kpi_etl;
    PRINT '>> Re-granted subscription view to db_kpi_etl';
END
GO
