/* =============================================================
   21_user_security.sql
   Purpose : ธงบังคับเปลี่ยนรหัสผ่านตอน login ครั้งถัดไป
             ใช้กับรหัสที่ Admin เป็นคนตั้งให้ (สร้างบัญชีใหม่ / reset)
             เพื่อให้รหัสที่ Admin รู้ ใช้ได้เพียงครั้งเดียว
   Idempotent : YES

   ทำไมไม่เพิ่มคอลัมน์ใน dbo.AspNetUsers ตรง ๆ
   ---------------------------------------------------------------
   โปรเจกต์นี้ไม่ได้เปิด EF Code First Migrations ไว้
   การเพิ่ม property ใน ApplicationUser จะทำให้ EF มองว่า model
   ไม่ตรงกับฐานข้อมูล แล้วโยน exception ตอนแอปเริ่มทำงาน
   เก็บไว้ในตารางของเราเองใน schema meta จึงไม่ไปยุ่งกับ schema
   ที่ ASP.NET Identity เป็นเจ้าของ และอ่าน/เขียนด้วย Dapper ได้ตรง ๆ
   ============================================================= */

USE KpiMonthlyReport;
GO

IF OBJECT_ID('meta.UserSecurity') IS NULL
BEGIN
CREATE TABLE meta.UserSecurity
(
    UserId              NVARCHAR(128)   NOT NULL,
    MustChangePassword  BIT             NOT NULL CONSTRAINT DF_UserSec_MustChange DEFAULT (0),

    /* ใครเป็นคนตั้งธงนี้ และเมื่อไหร่ — ไว้ไล่ดูคู่กับ meta.AuditLog */
    SetByUserName       NVARCHAR(256)   NULL,
    UpdatedAt           DATETIME2(0)    NOT NULL CONSTRAINT DF_UserSec_Updated DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_UserSecurity PRIMARY KEY CLUSTERED (UserId)
);
PRINT '>> Created meta.UserSecurity';
END
ELSE
    PRINT '>> meta.UserSecurity already exists';
GO

/* -------------------------------------------------------------
   สิทธิ์ของ role ที่เว็บใช้

   01_database_and_schemas.sql ให้ db_kpi_web แค่ SELECT, INSERT บน meta
   แต่หน้าจัดการผู้ใช้ต้อง UPDATE/DELETE ด้วย:
     - meta.UserSecurity   : ปลดธงเมื่อผู้ใช้เปลี่ยนรหัสแล้ว
     - meta.UserDepartment : ย้าย Viewer ข้ามแผนก (ลบของเดิมก่อนใส่ใหม่)

   ให้สิทธิ์เฉพาะ 2 ตารางนี้ ไม่เปิดทั้ง schema
   ตอนพัฒนาบนเครื่องตัวเองมักไม่เจอปัญหาเพราะเชื่อมด้วยสิทธิ์ผู้ดูแล
   แต่บนเซิร์ฟเวอร์จริงที่ใช้ role นี้จะพังถ้าไม่ให้สิทธิ์ตรงนี้
   ------------------------------------------------------------- */
IF DATABASE_PRINCIPAL_ID('db_kpi_web') IS NOT NULL
BEGIN
    GRANT UPDATE, DELETE ON meta.UserSecurity   TO db_kpi_web;
    GRANT UPDATE, DELETE ON meta.UserDepartment TO db_kpi_web;
    PRINT '>> Granted UPDATE/DELETE on meta.UserSecurity, meta.UserDepartment to db_kpi_web';
END
GO
