/* =============================================================
   25_remove_production_kpi.sql
   Purpose : ถอด KPI ฝั่งการผลิตออกจากระบบให้หมด
             (PROD_OUTPUT, DEFECT_RATE, DOWNTIME_HRS,
              COST_PER_UNIT, COST_DOWN_PCT)
             เหลือเฉพาะ KPI บุคลากรซึ่งเป็นขอบเขตจริงของระบบ
   Idempotent : YES (รันซ้ำได้ ของที่ถูกลบไปแล้วจะข้าม)

   *** สคริปต์นี้ลบข้อมูลถาวร — สำรองฐานข้อมูลก่อนรัน ***

   ด่านตรวจก่อนลบ
   ---------------------------------------------------------------
   ถ้า KPI การผลิตตัวใดยัง IsActive = 1 หรือ KPI บุคลากรยังไม่พร้อม
   สคริปต์จะหยุดทันทีด้วย SET NOEXEC ON โดยไม่ลบอะไรเลยแม้แต่อย่างเดียว
   เพราะการเผลอรันบนฐานข้อมูลที่ยังใช้ KPI การผลิตอยู่ = ข้อมูลหายถาวร

   ติดตั้งฐานข้อมูลใหม่ตั้งแต่ต้น
   ---------------------------------------------------------------
   รันตามลำดับเลข แต่ข้าม 09, 10, 12, 13 และ 11 ได้เลย
   (เป็นของฝั่งการผลิตทั้งหมด และไม่ต้องรัน 25 ตามด้วย)

   ถ้ารัน 07_seed.sql ไปแล้ว KPI การผลิตจะถูกสร้างพร้อม IsActive = 1
   ด่านตรวจด้านล่างจะบล็อกสคริปต์นี้ ให้ปิดก่อนด้วยคำสั่งนี้:

     UPDATE meta.KpiDefinition SET IsActive = 0
     WHERE KpiCode IN ('PROD_OUTPUT','DEFECT_RATE','DOWNTIME_HRS',
                       'COST_PER_UNIT','COST_DOWN_PCT');

   สิ่งที่ "ไม่" ถูกแตะ (กลไกกลาง — ต้องอยู่ต่อ)
   ---------------------------------------------------------------
   meta.KpiDefinition · meta.KpiTarget · core.FactKpiMonthly
   core.usp_RunKpi_Monthly (11a) · rpt.vw_KpiMonthly
   rpt.usp_GetKpiDashboard · rpt.usp_GetKpiTrend · rpt.vw_ValidMonth
   -> การเพิ่ม KPI บุคลากรตัวใหม่ในอนาคตยังทำได้เหมือนเดิมทุกประการ
   ============================================================= */

USE KpiMonthlyReport;
GO

/* =============================================================
   ด่านที่ 1 : KPI การผลิตต้องถูกปิดไว้แล้วทั้งหมด
   ============================================================= */
IF EXISTS (
    SELECT 1 FROM meta.KpiDefinition
    WHERE KpiCode IN ('PROD_OUTPUT','DEFECT_RATE','DOWNTIME_HRS',
                      'COST_PER_UNIT','COST_DOWN_PCT')
      AND IsActive = 1
)
BEGIN
    RAISERROR(N'หยุด: ยังมี KPI การผลิตที่ IsActive = 1 อยู่ ตั้งเป็น 0 และตรวจ Dashboard ให้เรียบร้อยก่อน แล้วค่อยรันสคริปต์นี้ใหม่ (ยังไม่มีอะไรถูกลบ)', 16, 1);
    SET NOEXEC ON;
END
GO

/* =============================================================
   ด่านที่ 2 : ต้องมี KPI บุคลากรที่ใช้งานอยู่จริง
   กันกรณีรันผิดฐานข้อมูล แล้วเหลือระบบที่ไม่มี KPI เลยสักตัว
   ============================================================= */
IF NOT EXISTS (
    SELECT 1 FROM meta.KpiDefinition
    WHERE CategoryName = N'HR' AND IsActive = 1
)
BEGIN
    RAISERROR(N'หยุด: ไม่พบ KPI หมวด HR ที่เปิดใช้งานอยู่ ฐานข้อมูลนี้อาจไม่ใช่ตัวที่ตั้งใจ (ยังไม่มีอะไรถูกลบ)', 16, 1);
    SET NOEXEC ON;
END
GO

PRINT '>> ผ่านด่านตรวจ เริ่มถอด KPI การผลิต';
GO

/* =============================================================
   1) แก้ proc ที่อ้างถึงตารางที่กำลังจะลบ  ** ทำก่อนลบเสมอ **
   ============================================================= */

/* usp_RunKpi_AllMonths เดิม UNION จาก FactProduction/Cost/Downtime
   ถ้าลบตารางก่อนแก้ proc = คำสั่ง kpi-all และ run-all พังทันที */
CREATE OR ALTER PROCEDURE core.usp_RunKpi_AllMonths
    @TriggeredBy NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @m INT;
    /* ไล่จากเก่าไปใหม่ เพราะ PrevMonthValue ต้องมีของเดือนก่อนแล้ว */
    DECLARE m_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT DISTINCT MonthKey FROM core.FactAttendance ORDER BY 1;

    OPEN m_cur;
    FETCH NEXT FROM m_cur INTO @m;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC core.usp_RunKpi_Monthly @MonthKey = @m, @TriggeredBy = @TriggeredBy;
        FETCH NEXT FROM m_cur INTO @m;
    END
    CLOSE m_cur;
    DEALLOCATE m_cur;
END
GO

/* usp_PurgeMonth เดิมลบจาก 3 ตารางที่กำลังจะหายไป
   และเดิม "ลืม" ลบ FactAttendance ทั้งที่เป็นข้อมูลหลักของระบบตอนนี้
   ถือโอกาสแก้ให้ครบในคราวเดียว */
CREATE OR ALTER PROCEDURE core.usp_PurgeMonth
    @MonthKey INT
AS
BEGIN
    SET NOCOUNT ON;
    IF @MonthKey IS NULL
    BEGIN
        RAISERROR('MonthKey is required.', 16, 1);
        RETURN;
    END

    BEGIN TRY
        BEGIN TRAN;
            DELETE FROM core.FactKpiMonthly  WHERE MonthKey = @MonthKey;
            DELETE FROM core.FactAttendance  WHERE MonthKey = @MonthKey;
        COMMIT TRAN;
        PRINT CONCAT('>> Purged month ', @MonthKey);
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO

/* =============================================================
   2) ลบ view รายงานฝั่งการผลิต
   ไม่มีโค้ดใดเรียกใช้ — เว็บและงานส่งอีเมลใช้แค่
   vw_KpiMonthly, vw_Department, vw_ValidMonth,
   usp_GetKpiDashboard, usp_GetKpiTrend
   ============================================================= */
DROP VIEW IF EXISTS rpt.vw_ProductionSummary;
DROP VIEW IF EXISTS rpt.vw_CostSummary;
DROP VIEW IF EXISTS rpt.vw_CostDown;
GO

/* =============================================================
   3) ลบ calc proc ของ KPI การผลิต (จาก 11_kpi_procs.sql)
   ============================================================= */
DROP PROCEDURE IF EXISTS core.usp_CalcKpi_ProductionOutput;
DROP PROCEDURE IF EXISTS core.usp_CalcKpi_DefectRate;
DROP PROCEDURE IF EXISTS core.usp_CalcKpi_DowntimeHours;
DROP PROCEDURE IF EXISTS core.usp_CalcKpi_CostPerUnit;
DROP PROCEDURE IF EXISTS core.usp_CalcKpi_CostDown;
GO

/* =============================================================
   4) ลบ proc ของ ETL การผลิต / เครื่องหยุด / ต้นทุน
   ============================================================= */
DROP PROCEDURE IF EXISTS core.usp_RunEtl_Production;
DROP PROCEDURE IF EXISTS core.usp_Transform_Production;
DROP PROCEDURE IF EXISTS stg.usp_Extract_Production;
DROP PROCEDURE IF EXISTS core.usp_Transform_Downtime;
DROP PROCEDURE IF EXISTS core.usp_Transform_Cost;
GO

/* =============================================================
   5) ลบข้อมูล KPI การผลิตออกจากตารางกลาง
   ต้องลบลูกก่อนแม่ ไม่งั้นติด foreign key
   ============================================================= */
;WITH dead AS (
    SELECT KpiId FROM meta.KpiDefinition
    WHERE KpiCode IN ('PROD_OUTPUT','DEFECT_RATE','DOWNTIME_HRS',
                      'COST_PER_UNIT','COST_DOWN_PCT')
)
DELETE f
FROM core.FactKpiMonthly f
JOIN dead d ON d.KpiId = f.KpiId;
PRINT CONCAT('>> ลบผลคำนวณ KPI การผลิต ', @@ROWCOUNT, ' แถว');
GO

DELETE t
FROM meta.KpiTarget t
JOIN meta.KpiDefinition k ON k.KpiId = t.KpiId
WHERE k.KpiCode IN ('PROD_OUTPUT','DEFECT_RATE','DOWNTIME_HRS',
                    'COST_PER_UNIT','COST_DOWN_PCT');
PRINT CONCAT('>> ลบเป้าหมาย KPI การผลิต ', @@ROWCOUNT, ' แถว');
GO

DELETE FROM meta.KpiDefinition
WHERE KpiCode IN ('PROD_OUTPUT','DEFECT_RATE','DOWNTIME_HRS',
                  'COST_PER_UNIT','COST_DOWN_PCT');
PRINT CONCAT('>> ลบนิยาม KPI การผลิต ', @@ROWCOUNT, ' แถว');
GO

/* =============================================================
   6) ลบตาราง fact / dim / staging ของฝั่งการผลิต
   ลบ fact ก่อน dim เสมอ เพราะ fact เป็นฝั่งที่ถือ foreign key

   ไม่ลบ: core.DimDate, core.DimDepartment, core.DepartmentAlias,
          stg.FileLoadHistory  -> ฝั่ง HR ใช้ร่วมกันอยู่
   ============================================================= */
DROP TABLE IF EXISTS core.FactCost;
DROP TABLE IF EXISTS core.FactDowntime;
DROP TABLE IF EXISTS core.FactProduction;
GO

DROP TABLE IF EXISTS core.DimCostType;
DROP TABLE IF EXISTS core.DimProduct;
GO

DROP TABLE IF EXISTS stg.CostRaw;
DROP TABLE IF EXISTS stg.DowntimeRaw;
DROP TABLE IF EXISTS stg.ProductionRaw;
GO

PRINT '>> ถอด KPI การผลิตออกจากฐานข้อมูลเรียบร้อย';
GO

/* =============================================================
   7) ตรวจผล
   ============================================================= */
SELECT KpiCode, KpiNameTh, CategoryName, IsActive, CalcProcName
FROM   meta.KpiDefinition
ORDER  BY SortOrder;

SELECT COUNT(*) AS RemainingKpiRows FROM core.FactKpiMonthly;
SELECT MAX(MonthKey) AS LatestValidMonth FROM rpt.vw_ValidMonth;
GO

SET NOEXEC OFF;
GO
