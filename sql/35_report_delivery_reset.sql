/* ============================================================
   35_report_delivery_reset.sql
   ------------------------------------------------------------
   ปุ่ม "รีเซ็ตรอบเดือน" ในหน้า log — ให้ Admin สั่งส่งรายงานของ
   เดือนที่ส่งไปแล้วใหม่อีกครั้งผ่านรอบอัตโนมัติ

   ทำไมไม่ลบแถว log
   ---------------------------------------------------------------
   DELETE ทำให้หลักฐานว่าเคยส่งไปแล้วหายไปทั้งหมด ซึ่งเป็นสิ่งที่
   หน้า log มีไว้ตอบตั้งแต่แรก จึงใช้วิธีทำเครื่องหมายว่าแถวนั้น
   "ถูกแทนที่แล้ว" (superseded) ประวัติยังอยู่ครบว่าเคยส่งเมื่อไร
   ใครกดรีเซ็ต และรีเซ็ตเมื่อไร

   ผลต่อการกันส่งซ้ำ
   ---------------------------------------------------------------
   ReportRepository.AlreadySent (ETL) เพิ่มเงื่อนไข SupersededAt IS NULL
   แถวที่ถูกรีเซ็ตจึงไม่นับเป็น "เคยส่งแล้ว" รอบชั่วโมงถัดไปที่ถึง
   กำหนดจะส่งให้ใหม่ และเขียนแถว log ใบใหม่ต่อท้าย

   รันซ้ำได้ (idempotent)
   ต้องรันหลัง 34_report_delivery_log_detail.sql
   ============================================================ */
USE KpiMonthlyReport;
GO

/* ---------- คอลัมน์ใหม่ ---------- */

IF COL_LENGTH('meta.ReportDeliveryLog', 'SupersededAt') IS NULL
BEGIN
    ALTER TABLE meta.ReportDeliveryLog ADD SupersededAt DATETIME2(0) NULL;
    PRINT '>> Added ReportDeliveryLog.SupersededAt';
END
GO

IF COL_LENGTH('meta.ReportDeliveryLog', 'SupersededBy') IS NULL
BEGIN
    ALTER TABLE meta.ReportDeliveryLog ADD SupersededBy NVARCHAR(256) NULL;
    PRINT '>> Added ReportDeliveryLog.SupersededBy';
END
GO

/*  index ของการกันส่งซ้ำ: ETL ถามคำถามเดียวทุกรอบชั่วโมง —
    "เดือนนี้ รายงานชื่อนี้ ส่งสำเร็จและยังไม่ถูกรีเซ็ต มีหรือยัง"
    filtered index ตามเงื่อนไขนั้นเลย จะได้ไม่สแกนแถวที่ไม่เกี่ยว
    (Recipients เป็น NVARCHAR(MAX) จึงเป็น key ไม่ได้ ต้องให้ ETL
     กรองอีเมลจากแถวที่เหลือเอง ซึ่งมีไม่กี่แถวต่อเดือน)            */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ReportDelivery_Active')
BEGIN
    CREATE INDEX IX_ReportDelivery_Active
        ON meta.ReportDeliveryLog(MonthKey, ReportName)
        WHERE Status = 'SENT' AND SupersededAt IS NULL;
    PRINT '>> Created IX_ReportDelivery_Active';
END
GO

/* ---------- view ---------- */

IF OBJECT_ID('meta.vw_ReportDeliveryLog') IS NOT NULL
    DROP VIEW meta.vw_ReportDeliveryLog;
GO

CREATE VIEW meta.vw_ReportDeliveryLog
AS
SELECT  d.DeliveryId,
        d.SubscriptionId,
        d.MonthKey,
        d.ReportName,
        d.FileFormat,
        d.FileSizeBytes,
        d.Recipients                    AS Email,
        sub.DisplayName,
        sub.DepartmentIds               AS CurrentDepartmentIds,
        sub.DepartmentCount             AS CurrentDepartmentCount,
        COALESCE(d.ScopeLabel, N'-')    AS ScopeLabel,
        d.TriggerType,
        d.TriggeredBy,
        d.Status,
        d.ErrorMessage,
        d.RetryCount,
        d.CreatedAt,
        d.SentAt,
        d.SupersededAt,
        d.SupersededBy
FROM    meta.ReportDeliveryLog AS d
LEFT JOIN meta.vw_ReportSubscriptionAdmin AS sub
       ON sub.SubscriptionId = d.SubscriptionId;
GO
PRINT '>> Recreated meta.vw_ReportDeliveryLog with superseded columns';
GO

IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'db_kpi_web')
BEGIN
    GRANT SELECT ON meta.vw_ReportDeliveryLog TO db_kpi_web;
    PRINT '>> Granted meta.vw_ReportDeliveryLog to db_kpi_web';
END
GO
