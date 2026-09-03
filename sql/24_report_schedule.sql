/* =============================================================
   24_report_schedule.sql
   Purpose : ตั้งเวลาส่งรายงานได้ต่อผู้รับ — วันที่เท่าไหร่ของเดือน เวลากี่โมง
   Idempotent : YES
   ต้องรันหลัง 23_report_subscription_user_link.sql

   ทำไมเก็บตารางเวลาไว้ในฐานข้อมูล ไม่ใช่ตั้งใน Task Scheduler
   ---------------------------------------------------------------
   ถ้าฝากตารางเวลาไว้กับ Task Scheduler ทุกคนจะได้รายงานพร้อมกันหมด
   และการเปลี่ยนเวลาต้องเข้าไปที่เซิร์ฟเวอร์ ซึ่ง HR ทำเองไม่ได้

   วิธีนี้ให้ Task Scheduler เรียกโปรแกรม "ทุกชั่วโมง" อย่างเดียว
   แล้วโปรแกรมเป็นคนตัดสินเองว่าถึงกำหนดของใครแล้วบ้าง
   ผู้ดูแลจึงแก้เวลาได้จากหน้าเว็บโดยไม่ต้องแตะเซิร์ฟเวอร์เลย
   ============================================================= */

USE KpiMonthlyReport;
GO

/* ---------- 1) คอลัมน์ตารางเวลา ---------- */
IF COL_LENGTH('meta.ReportSubscription', 'SendDayOfMonth') IS NULL
BEGIN
    /* ค่าเริ่มต้นวันที่ 3 : เผื่อเวลาให้ ETL ปิดยอดเดือนก่อนหน้าเสร็จก่อน */
    ALTER TABLE meta.ReportSubscription
        ADD SendDayOfMonth TINYINT NOT NULL
            CONSTRAINT DF_ReportSub_SendDay DEFAULT (3);
    PRINT '>> Added meta.ReportSubscription.SendDayOfMonth';
END
GO

IF COL_LENGTH('meta.ReportSubscription', 'SendHour') IS NULL
BEGIN
    /* ค่าเริ่มต้น 08:00 ตามเวลาเครื่องที่รันงาน */
    ALTER TABLE meta.ReportSubscription
        ADD SendHour TINYINT NOT NULL
            CONSTRAINT DF_ReportSub_SendHour DEFAULT (8);
    PRINT '>> Added meta.ReportSubscription.SendHour';
END
GO

/* ยอมรับถึงวันที่ 31 แม้บางเดือนจะไม่มี — ฝั่งโปรแกรมจะร่นลงมา
   เป็นวันสุดท้ายของเดือนนั้นให้เอง (เช่นตั้ง 31 เดือนกุมภาพันธ์
   จะส่งวันที่ 28 หรือ 29) ถ้าห้ามไว้ตรงนี้ ผู้ใช้ที่ตั้งใจว่า
   "สิ้นเดือน" จะตั้งค่าไม่ได้เลย */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ReportSub_SendDay')
    ALTER TABLE meta.ReportSubscription
        ADD CONSTRAINT CK_ReportSub_SendDay CHECK (SendDayOfMonth BETWEEN 1 AND 31);
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ReportSub_SendHour')
    ALTER TABLE meta.ReportSubscription
        ADD CONSTRAINT CK_ReportSub_SendHour CHECK (SendHour BETWEEN 0 AND 23);
GO

/* ---------- 2) เพิ่มคอลัมน์เข้า view ทั้งสองตัว ---------- */
CREATE OR ALTER VIEW meta.vw_ActiveReportSubscription
AS
SELECT  s.SubscriptionId,
        COALESCE(u.Email, u.UserName, s.Email)       AS Email,
        COALESCE(s.DisplayName, u.UserName, s.Email) AS DisplayName,
        s.DepartmentId,
        d.DepartmentName,
        s.SendDayOfMonth,
        s.SendHour
FROM        meta.ReportSubscription s
LEFT JOIN   dbo.AspNetUsers         u ON u.Id = s.UserId
LEFT JOIN   core.DimDepartment      d ON d.DepartmentId = s.DepartmentId
WHERE   s.IsActive = 1
        AND (
              s.UserId IS NULL
              OR (u.Id IS NOT NULL
                  AND (u.LockoutEndDateUtc IS NULL
                       OR u.LockoutEndDateUtc <= SYSUTCDATETIME()))
            );
GO

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
        s.SendDayOfMonth,
        s.SendHour,
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

PRINT '>> Report schedule ready (SendDayOfMonth, SendHour)';
GO
