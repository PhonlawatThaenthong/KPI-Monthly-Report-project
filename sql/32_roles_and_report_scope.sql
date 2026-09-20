/* =============================================================
   32_roles_and_report_scope.sql
   Purpose : 1) เหลือ role แค่ Admin กับ Manager
             2) ผู้รับรายงาน 1 คน เลือกได้หลายแผนกในรายงานฉบับเดียว

   Idempotent : YES (รันซ้ำได้)
   ต้องรันหลัง 23_report_subscription_user_link.sql และ 31_org_seed.sql
   ต้องการ SQL Server 2017 ขึ้นไป (ใช้ STRING_AGG / STRING_SPLIT)

   ปัญหาที่แก้
   ---------------------------------------------------------------
   เดิม meta.ReportSubscription เก็บ DepartmentId ได้ค่าเดียวต่อแถว
   ผู้จัดการที่ดูแล 3 แผนกจึงต้องมี 3 แถว = ได้อีเมล 3 ฉบับต่อเดือน
   และเวลาจะแก้ขอบเขตต้องไล่แก้ทีละแถว

   วิธีแก้
   ---------------------------------------------------------------
   ย้ายขอบเขตออกไปเป็นตารางลูก meta.ReportSubscriptionDepartment
     มีแถวลูก    -> ส่งเฉพาะแผนกที่เลือก (กี่แผนกก็ได้) ในอีเมลฉบับเดียว
     ไม่มีแถวลูก -> ทุกแผนก (ค่าเริ่มต้นเดิม)
   แล้วลบคอลัมน์ DepartmentId ทิ้ง ไม่เก็บไว้ทั้งสองที่ เพราะข้อมูล
   ขอบเขตสองแหล่งคือที่มาของบั๊กที่ไม่มีใครตอบได้ว่าอันไหนถูก
   ============================================================= */

USE KpiMonthlyReport;
GO

IF OBJECT_ID('meta.ReportSubscription') IS NULL
BEGIN
    RAISERROR(N'หยุด: ไม่พบ meta.ReportSubscription (ต้องรัน 22 และ 23 ก่อน)', 16, 1);
    SET NOEXEC ON;
END
GO

/* =============================================================
   1) ตารางขอบเขตแผนกของรายงาน
   ============================================================= */
IF OBJECT_ID('meta.ReportSubscriptionDepartment') IS NULL
BEGIN
CREATE TABLE meta.ReportSubscriptionDepartment
(
    SubscriptionId  INT           NOT NULL,
    DepartmentId    INT           NOT NULL,
    CreatedAt       DATETIME2(0)  NOT NULL
        CONSTRAINT DF_ReportSubDept_Created DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_ReportSubscriptionDepartment
        PRIMARY KEY CLUSTERED (SubscriptionId, DepartmentId),
    /* ลบผู้รับ = ขอบเขตของเขาหายตาม ไม่ต้องให้เว็บไล่ลบเอง
       (แถวลูกค้างโดยไม่มีแม่คือข้อมูลขยะที่ไม่มีใครเห็น) */
    CONSTRAINT FK_ReportSubDept_Sub FOREIGN KEY (SubscriptionId)
        REFERENCES meta.ReportSubscription(SubscriptionId) ON DELETE CASCADE,
    CONSTRAINT FK_ReportSubDept_Dept FOREIGN KEY (DepartmentId)
        REFERENCES core.DimDepartment(DepartmentId)
);
CREATE INDEX IX_ReportSubDept_Dept ON meta.ReportSubscriptionDepartment(DepartmentId);
PRINT '>> Created meta.ReportSubscriptionDepartment';
END
ELSE
    PRINT '>> meta.ReportSubscriptionDepartment already exists';
GO

/* =============================================================
   2) ย้ายขอบเขตเดิมเข้าตารางใหม่ แล้วเลิกใช้คอลัมน์เก่า
   ============================================================= */
IF COL_LENGTH('meta.ReportSubscription', 'DepartmentId') IS NOT NULL
BEGIN
    INSERT INTO meta.ReportSubscriptionDepartment (SubscriptionId, DepartmentId)
    SELECT s.SubscriptionId, s.DepartmentId
    FROM meta.ReportSubscription s
    WHERE s.DepartmentId IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM meta.ReportSubscriptionDepartment x
                      WHERE x.SubscriptionId = s.SubscriptionId
                        AND x.DepartmentId   = s.DepartmentId);
    PRINT CONCAT('>> ย้ายขอบเขตเดิม ', @@ROWCOUNT, ' แถวเข้าตารางใหม่');
END
GO

/* index ที่อ้างคอลัมน์เก่าต้องไปก่อน ไม่งั้น DROP COLUMN ไม่ผ่าน */
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ReportSub_Key_Dept'
           AND object_id = OBJECT_ID('meta.ReportSubscription'))
    DROP INDEX UX_ReportSub_Key_Dept ON meta.ReportSubscription;
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ReportSub_Key_AllDept'
           AND object_id = OBJECT_ID('meta.ReportSubscription'))
    DROP INDEX UX_ReportSub_Key_AllDept ON meta.ReportSubscription;
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ReportSub_Email_Dept'
           AND object_id = OBJECT_ID('meta.ReportSubscription'))
    DROP INDEX UX_ReportSub_Email_Dept ON meta.ReportSubscription;
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ReportSub_Email_AllDept'
           AND object_id = OBJECT_ID('meta.ReportSubscription'))
    DROP INDEX UX_ReportSub_Email_AllDept ON meta.ReportSubscription;
GO

/* view เก่าอ้างคอลัมน์นี้อยู่ ต้องสร้างใหม่หลังลบคอลัมน์ (ข้อ 3) */
IF COL_LENGTH('meta.ReportSubscription', 'DepartmentId') IS NOT NULL
BEGIN
    DECLARE @fk SYSNAME =
        (SELECT TOP 1 fk.name
         FROM sys.foreign_keys fk
         JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
         JOIN sys.columns c ON c.object_id = fkc.parent_object_id
                           AND c.column_id = fkc.parent_column_id
         WHERE fk.parent_object_id = OBJECT_ID('meta.ReportSubscription')
           AND c.name = 'DepartmentId');
    IF @fk IS NOT NULL
        EXEC('ALTER TABLE meta.ReportSubscription DROP CONSTRAINT ' + @fk);

    ALTER TABLE meta.ReportSubscription DROP COLUMN DepartmentId;
    PRINT '>> ลบคอลัมน์ meta.ReportSubscription.DepartmentId (ขอบเขตย้ายไปตารางลูกแล้ว)';
END
GO

/* ผู้รับ 1 คน = 1 แถวเท่านั้นแล้ว ขอบเขตอยู่ในตารางลูก */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ReportSub_Key'
               AND object_id = OBJECT_ID('meta.ReportSubscription'))
BEGIN
    /* ถ้าของเดิมมีคนซ้ำหลายแถว (คนละแผนก) ต้องยุบก่อน ไม่งั้นสร้าง index ไม่ผ่าน */
    ;WITH dup AS (
        SELECT SubscriptionId, DedupeKey,
               ROW_NUMBER() OVER (PARTITION BY DedupeKey ORDER BY SubscriptionId) AS rn
        FROM meta.ReportSubscription
    )
    /* ขอบเขตของแถวที่จะถูกยุบ ย้ายไปรวมกับแถวแรกของคนเดียวกัน */
    INSERT INTO meta.ReportSubscriptionDepartment (SubscriptionId, DepartmentId)
    SELECT keep.SubscriptionId, sd.DepartmentId
    FROM dup d
    JOIN dup keep ON keep.DedupeKey = d.DedupeKey AND keep.rn = 1
    JOIN meta.ReportSubscriptionDepartment sd ON sd.SubscriptionId = d.SubscriptionId
    WHERE d.rn > 1
      AND NOT EXISTS (SELECT 1 FROM meta.ReportSubscriptionDepartment x
                      WHERE x.SubscriptionId = keep.SubscriptionId
                        AND x.DepartmentId   = sd.DepartmentId);

    ;WITH dup AS (
        SELECT SubscriptionId,
               ROW_NUMBER() OVER (PARTITION BY DedupeKey ORDER BY SubscriptionId) AS rn
        FROM meta.ReportSubscription
    )
    DELETE s
    FROM meta.ReportSubscription s
    JOIN dup d ON d.SubscriptionId = s.SubscriptionId
    WHERE d.rn > 1;

    CREATE UNIQUE INDEX UX_ReportSub_Key ON meta.ReportSubscription(DedupeKey);
    PRINT '>> Created UX_ReportSub_Key (ผู้รับ 1 คน = 1 แถว)';
END
GO

/* =============================================================
   3) สร้าง view ใหม่ — ขอบเขตกลายเป็นรายการแผนก
      DepartmentIds : '3,5,8' ส่งต่อให้ rpt.usp_* ใช้ได้ตรง ๆ
                      NULL = ทุกแผนก
   ============================================================= */
CREATE OR ALTER VIEW meta.vw_ActiveReportSubscription
AS
SELECT  s.SubscriptionId,

        /* ผูก user แล้วให้ยึดอีเมลของบัญชีเป็นหลักเสมอ
           เปลี่ยนอีเมลในบัญชี รายงานก็ตามไปเอง ไม่ต้องแก้สองที่ */
        COALESCE(u.Email, u.UserName, s.Email)       AS Email,
        COALESCE(s.DisplayName, u.UserName, s.Email) AS DisplayName,

        sc.DepartmentIds,
        sc.DepartmentNames,
        ISNULL(sc.DepartmentCount, 0)                AS DepartmentCount,

        s.SendDayOfMonth,
        s.SendHour
FROM        meta.ReportSubscription s
LEFT JOIN   dbo.AspNetUsers         u ON u.Id = s.UserId
OUTER APPLY (
    SELECT  STRING_AGG(CAST(d.DepartmentId AS NVARCHAR(10)), ',')
                WITHIN GROUP (ORDER BY d.DepartmentCode) AS DepartmentIds,
            STRING_AGG(d.DepartmentName, N', ')
                WITHIN GROUP (ORDER BY d.DepartmentCode) AS DepartmentNames,
            COUNT(*)                                     AS DepartmentCount
    FROM meta.ReportSubscriptionDepartment sd
    JOIN core.DimDepartment d ON d.DepartmentId = sd.DepartmentId
    WHERE sd.SubscriptionId = s.SubscriptionId
      AND d.IsActive = 1
) sc
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

CREATE OR ALTER VIEW meta.vw_ReportSubscriptionAdmin
AS
SELECT  s.SubscriptionId,
        s.UserId,
        COALESCE(u.Email, u.UserName, s.Email)       AS Email,
        COALESCE(s.DisplayName, u.UserName, s.Email) AS DisplayName,
        sc.DepartmentIds,
        sc.DepartmentNames,
        ISNULL(sc.DepartmentCount, 0)                AS DepartmentCount,
        s.SendDayOfMonth,
        s.SendHour,
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
OUTER APPLY (
    SELECT  STRING_AGG(CAST(d.DepartmentId AS NVARCHAR(10)), ',')
                WITHIN GROUP (ORDER BY d.DepartmentCode) AS DepartmentIds,
            STRING_AGG(d.DepartmentName, N', ')
                WITHIN GROUP (ORDER BY d.DepartmentCode) AS DepartmentNames,
            COUNT(*)                                     AS DepartmentCount
    FROM meta.ReportSubscriptionDepartment sd
    JOIN core.DimDepartment d ON d.DepartmentId = sd.DepartmentId
    WHERE sd.SubscriptionId = s.SubscriptionId
) sc;
GO

PRINT '>> สร้าง view ผู้รับรายงานใหม่ (รองรับหลายแผนกต่อหนึ่งฉบับ)';
GO

/* =============================================================
   4) Role : เหลือ Admin กับ Manager
      Viewer ที่เหลืออยู่เลื่อนเป็น Manager — สิทธิ์จริงคุมด้วย
      meta.UserDepartment อยู่แล้ว (Manager เห็นเฉพาะแผนกที่ถูกผูกไว้)
      จึงไม่ได้เปิดสิทธิ์ให้ใครกว้างขึ้นจากการเลื่อนครั้งนี้
   ============================================================= */
IF OBJECT_ID('dbo.AspNetRoles') IS NOT NULL
BEGIN
    DECLARE @viewerId NVARCHAR(128) = (SELECT TOP 1 Id FROM dbo.AspNetRoles WHERE Name = 'Viewer');
    DECLARE @mgrId    NVARCHAR(128) = (SELECT TOP 1 Id FROM dbo.AspNetRoles WHERE Name = 'Manager');

    IF @viewerId IS NOT NULL AND @mgrId IS NOT NULL
    BEGIN
        INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
        SELECT ur.UserId, @mgrId
        FROM dbo.AspNetUserRoles ur
        WHERE ur.RoleId = @viewerId
          AND NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles x
                          WHERE x.UserId = ur.UserId AND x.RoleId = @mgrId);
        PRINT CONCAT('>> เลื่อน Viewer เป็น Manager ', @@ROWCOUNT, ' บัญชี');

        DELETE FROM dbo.AspNetUserRoles WHERE RoleId = @viewerId;
    END

    IF @viewerId IS NOT NULL
    BEGIN
        DELETE FROM dbo.AspNetRoles WHERE Id = @viewerId;
        PRINT '>> ลบ role Viewer';
    END
END
ELSE
    PRINT '>> ข้ามขั้นตอน role: ยังไม่มีตาราง Identity (เว็บยังไม่เคยรัน)';
GO

/* Manager ทุกคนต้องมีแผนกผูกไว้อย่างน้อยหนึ่งแผนก ไม่งั้นเข้าเว็บมาแล้ว
   จะไม่เห็นอะไรเลยและดูเหมือนระบบพัง — ตรวจให้ผู้ดูแลเห็นตั้งแต่ตอนติดตั้ง */
IF OBJECT_ID('dbo.AspNetUsers') IS NOT NULL
SELECT  u.UserName AS ManagerWithoutDepartment
FROM    dbo.AspNetUsers     u
JOIN    dbo.AspNetUserRoles ur ON ur.UserId = u.Id
JOIN    dbo.AspNetRoles     r  ON r.Id = ur.RoleId AND r.Name = 'Manager'
WHERE   NOT EXISTS (SELECT 1 FROM meta.UserDepartment ud WHERE ud.UserId = u.Id);
GO

/* =============================================================
   5) Dashboard ที่รับได้หลายแผนกพร้อมกัน

   ของเดิม rpt.usp_GetKpiDashboard รับ @DepartmentId ได้ค่าเดียว
   พอผู้ใช้หนึ่งคนดูแลหลายแผนก เว็บต้องวนเรียกทีละแผนกแล้วเอามาต่อกันเอง
   ซึ่งทำให้ลำดับการเรียงเพี้ยนและยิง query ซ้ำโดยไม่จำเป็น

   ตัวเก่ายังอยู่ครบ — หน้าจอที่เลือกแผนกเดียวยังเรียกของเดิมได้เหมือนเดิม
   ============================================================= */
CREATE OR ALTER PROCEDURE rpt.usp_GetKpiDashboardMulti
    @MonthKey      INT,
    @DepartmentIds NVARCHAR(MAX) = NULL,   -- '3,5,8'; NULL/ว่าง = ทุกแผนก
    @CategoryName  NVARCHAR(50)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM rpt.vw_KpiMonthly v
    WHERE v.MonthKey = @MonthKey
      AND (@DepartmentIds IS NULL OR LTRIM(RTRIM(@DepartmentIds)) = N''
           OR v.DepartmentId IN (SELECT TRY_CONVERT(INT, value)
                                 FROM STRING_SPLIT(@DepartmentIds, ',')))
      AND (@CategoryName IS NULL OR v.CategoryName = @CategoryName)
    ORDER BY v.DepartmentId, v.SortOrder, v.KpiCode;
END
GO

/* =============================================================
   6) สิทธิ์
   ============================================================= */
IF DATABASE_PRINCIPAL_ID('db_kpi_web') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, UPDATE, DELETE ON meta.ReportSubscriptionDepartment TO db_kpi_web;
    GRANT SELECT ON meta.vw_ReportSubscriptionAdmin TO db_kpi_web;
    GRANT SELECT ON rpt.vw_EmployeeKpiStatus        TO db_kpi_web;
    GRANT SELECT ON rpt.vw_EmployeeKpiSummary       TO db_kpi_web;
    GRANT SELECT ON rpt.vw_DepartmentKpiCompletion  TO db_kpi_web;
    GRANT EXECUTE ON rpt.usp_GetKpiDashboardMulti   TO db_kpi_web;
    GRANT EXECUTE ON rpt.usp_GetDepartmentMonitoring TO db_kpi_web;
    GRANT EXECUTE ON rpt.usp_GetEmployeeKpiStatus    TO db_kpi_web;
    GRANT EXECUTE ON rpt.usp_GetEmployeeKpiSummary   TO db_kpi_web;
    PRINT '>> Granted monitoring objects to db_kpi_web';
END
GO

IF DATABASE_PRINCIPAL_ID('db_kpi_etl') IS NOT NULL
BEGIN
    GRANT SELECT ON meta.vw_ActiveReportSubscription TO db_kpi_etl;
    GRANT SELECT, INSERT, UPDATE, DELETE ON stg.KpiEmployeeFeedRaw TO db_kpi_etl;
    GRANT SELECT ON rpt.vw_EmployeeKpiStatus         TO db_kpi_etl;
    GRANT SELECT ON rpt.vw_EmployeeKpiSummary        TO db_kpi_etl;
    GRANT SELECT ON rpt.vw_DepartmentKpiCompletion   TO db_kpi_etl;
    GRANT EXECUTE ON core.usp_Transform_KpiEmployeeFeed TO db_kpi_etl;
    GRANT EXECUTE ON core.usp_Rollup_KpiEmployeeToDept  TO db_kpi_etl;
    GRANT EXECUTE ON rpt.usp_GetKpiDashboardMulti       TO db_kpi_etl;
    GRANT EXECUTE ON rpt.usp_GetDepartmentMonitoring    TO db_kpi_etl;
    GRANT EXECUTE ON rpt.usp_GetEmployeeKpiStatus       TO db_kpi_etl;
    GRANT EXECUTE ON rpt.usp_GetEmployeeKpiSummary      TO db_kpi_etl;
    PRINT '>> Granted feed + rollup objects to db_kpi_etl';
END
GO

PRINT '>> 32_roles_and_report_scope.sql เสร็จสมบูรณ์';
GO

/* ปลด NOEXEC เสมอ ไม่งั้นสคริปต์ถัดไปในหน้าต่าง SSMS เดิมจะเงียบไปทั้งไฟล์ */
SET NOEXEC OFF;
GO
