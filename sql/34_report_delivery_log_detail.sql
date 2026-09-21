/* ============================================================
   34_report_delivery_log_detail.sql
   ------------------------------------------------------------
   เพิ่มรายละเอียดให้ meta.ReportDeliveryLog เพื่อให้ตอบได้ว่า
   "ใครกดส่งให้ใคร เมื่อไหร่ ขอบเขตไหน และมาจากรอบอัตโนมัติหรือกดมือ"

   ของเดิมมีแค่ MonthKey / ReportName / Recipients / Status จึงแยก
   รอบอัตโนมัติกับการกดส่งมือได้เฉพาะจาก prefix ของ ReportName
   ('KPI_Monthly_Manual:') และไม่รู้เลยว่าใครเป็นคนกด

   รันซ้ำได้ (idempotent)
   ============================================================ */
USE KpiMonthlyReport;
GO

/* ---------- คอลัมน์ใหม่ ---------- */

IF COL_LENGTH('meta.ReportDeliveryLog', 'SubscriptionId') IS NULL
BEGIN
    ALTER TABLE meta.ReportDeliveryLog ADD SubscriptionId INT NULL;
    PRINT '>> Added ReportDeliveryLog.SubscriptionId';
END
GO

/* SCHEDULED = รอบอัตโนมัติจาก ETL / MANUAL = ปุ่ม "ส่งเดี๋ยวนี้" ในเว็บ */
IF COL_LENGTH('meta.ReportDeliveryLog', 'TriggerType') IS NULL
BEGIN
    ALTER TABLE meta.ReportDeliveryLog
        ADD TriggerType VARCHAR(10) NOT NULL
            CONSTRAINT DF_Deliv_TriggerType DEFAULT ('SCHEDULED');
    PRINT '>> Added ReportDeliveryLog.TriggerType';
END
GO

/* อีเมล/ชื่อผู้ใช้ที่กดส่ง — NULL สำหรับรอบอัตโนมัติ */
IF COL_LENGTH('meta.ReportDeliveryLog', 'TriggeredBy') IS NULL
BEGIN
    ALTER TABLE meta.ReportDeliveryLog ADD TriggeredBy NVARCHAR(256) NULL;
    PRINT '>> Added ReportDeliveryLog.TriggeredBy';
END
GO

/* ขอบเขตแผนกของฉบับนั้น เก็บเป็นข้อความ ณ เวลาที่ส่ง
   ตั้งใจไม่ join กลับไปที่ subscription ตอนแสดงผล เพราะขอบเขตถูกแก้ได้
   ภายหลัง แล้ว log จะเล่าเรื่องผิดว่าฉบับเก่าครอบคลุมแผนกไหน */
IF COL_LENGTH('meta.ReportDeliveryLog', 'ScopeLabel') IS NULL
BEGIN
    ALTER TABLE meta.ReportDeliveryLog ADD ScopeLabel NVARCHAR(400) NULL;
    PRINT '>> Added ReportDeliveryLog.ScopeLabel';
END
GO

/* เวลาที่ "เริ่มพยายามส่ง" — คู่กับ SentAt ที่เป็นเวลาที่ส่งสำเร็จ
   แถวที่ค้าง PENDING จึงยังมีเวลาให้ไล่ย้อนได้ */
IF COL_LENGTH('meta.ReportDeliveryLog', 'CreatedAt') IS NULL
BEGIN
    ALTER TABLE meta.ReportDeliveryLog
        ADD CreatedAt DATETIME2(0) NOT NULL
            CONSTRAINT DF_Deliv_CreatedAt DEFAULT (SYSDATETIME());
    PRINT '>> Added ReportDeliveryLog.CreatedAt';
END
GO

/* ---------- backfill แถวเดิม ---------- */

UPDATE meta.ReportDeliveryLog
SET TriggerType = 'MANUAL'
WHERE ReportName LIKE 'KPI_Monthly_Manual:%'
  AND TriggerType <> 'MANUAL';
PRINT '>> Backfilled TriggerType from ReportName prefix';
GO

/* ขอบเขตของแถวเดิมอ่านได้จากส่วนหลัง ':' ของ ReportName */
UPDATE meta.ReportDeliveryLog
SET ScopeLabel = CASE
        WHEN RIGHT(ReportName, CHARINDEX(':', REVERSE(ReportName)) - 1) = 'ALL'
            THEN N'ทุกแผนก'
        ELSE RIGHT(ReportName, CHARINDEX(':', REVERSE(ReportName)) - 1)
    END
WHERE ScopeLabel IS NULL
  AND CHARINDEX(':', ReportName) > 0;
PRINT '>> Backfilled ScopeLabel from ReportName';
GO

/* ผูกแถวเดิมเข้ากับ subscription ที่อีเมลตรงกันและมีรายเดียว
   อีเมลของผู้รับที่ผูกบัญชีอยู่ใน AspNetUsers จึงเทียบผ่าน view ของ admin
   ที่รวมอีเมลจากทั้งสองทางไว้แล้ว — อีเมลที่ตรงหลาย subscription
   ปล่อยเป็น NULL ไว้ดีกว่าเดาผิด */
UPDATE d
SET SubscriptionId = m.SubscriptionId
FROM meta.ReportDeliveryLog AS d
CROSS APPLY (
    SELECT MIN(v.SubscriptionId) AS SubscriptionId, COUNT(*) AS Matches
    FROM meta.vw_ReportSubscriptionAdmin AS v
    WHERE v.Email = d.Recipients
) AS m
WHERE d.SubscriptionId IS NULL
  AND m.Matches = 1;
PRINT '>> Linked existing rows to ReportSubscription by email';
GO

/* ---------- constraint + index ---------- */

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ReportDelivery_TriggerType')
BEGIN
    ALTER TABLE meta.ReportDeliveryLog WITH NOCHECK
        ADD CONSTRAINT CK_ReportDelivery_TriggerType
            CHECK (TriggerType IN ('SCHEDULED', 'MANUAL'));
    PRINT '>> Created CK_ReportDelivery_TriggerType';
END
GO

/* หน้า log ต่อผู้รับเรียงใหม่ไปเก่าเสมอ index จึงไล่ตามนั้น */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ReportDelivery_Subscription')
BEGIN
    CREATE INDEX IX_ReportDelivery_Subscription
        ON meta.ReportDeliveryLog(SubscriptionId, DeliveryId DESC);
    PRINT '>> Created IX_ReportDelivery_Subscription';
END
GO

/*  ไม่มี index ตาม Recipients: คอลัมน์นั้นเป็น NVARCHAR(MAX) จึงเป็น key
    ของ index ไม่ได้ (Msg 1919) และ log รายเดือนมีไม่กี่พันแถว การสแกน
    เพื่อหาแถวเก่าที่ยังไม่มี SubscriptionId จึงถูกกว่าการแปลงชนิดคอลัมน์
    ที่มีข้อมูลอยู่แล้ว — แถวใหม่ทุกแถวมี SubscriptionId ติดมาเสมอ
    และหน้าจอไล่ด้วย IX_ReportDelivery_Subscription ข้างบน                */

/* ---------- view สำหรับหน้าจอ ---------- */

IF OBJECT_ID('meta.vw_ReportDeliveryLog') IS NOT NULL
    DROP VIEW meta.vw_ReportDeliveryLog;
GO

/*  หนึ่งแถว = ความพยายามส่ง 1 ฉบับ

    DisplayName เอามาจาก subscription ปัจจุบันเพื่อให้อ่านชื่อคนออก
    แต่ ScopeLabel ใช้ค่าที่บันทึกไว้ในแถว log ไม่ใช่ขอบเขตปัจจุบัน  */
CREATE VIEW meta.vw_ReportDeliveryLog
AS
SELECT  d.DeliveryId,
        d.SubscriptionId,
        d.MonthKey,
        d.ReportName,
        d.FileFormat,
        d.FileSizeBytes,
        d.Recipients                  AS Email,
        sub.DisplayName,
        sub.DepartmentIds               AS CurrentDepartmentIds,
        sub.DepartmentCount             AS CurrentDepartmentCount,
        COALESCE(d.ScopeLabel, N'-')  AS ScopeLabel,
        d.TriggerType,
        d.TriggeredBy,
        d.Status,
        d.ErrorMessage,
        d.RetryCount,
        d.CreatedAt,
        d.SentAt
FROM    meta.ReportDeliveryLog AS d
LEFT JOIN meta.vw_ReportSubscriptionAdmin AS sub
       ON sub.SubscriptionId = d.SubscriptionId;
GO
PRINT '>> Created meta.vw_ReportDeliveryLog';
GO

/* หน้าเว็บอ่าน log ผ่าน view นี้เท่านั้น */
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'db_kpi_web')
BEGIN
    GRANT SELECT ON meta.vw_ReportDeliveryLog TO db_kpi_web;
    PRINT '>> Granted meta.vw_ReportDeliveryLog to db_kpi_web';
END
GO
