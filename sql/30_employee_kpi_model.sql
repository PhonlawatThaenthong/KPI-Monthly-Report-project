/* =============================================================
   30_employee_kpi_model.sql
   Purpose : เปลี่ยนหน่วยข้อมูลของ KPI จาก "ระดับแผนก" เป็น "ระดับบุคคล"

             เดิม  : feed ส่งค่า KPI มาเป็นแผนก -> core.FactKpiMonthly
             ใหม่  : feed ส่ง KPI ของพนักงานรายคน -> core.FactKpiEmployeeMonthly
                     แล้วระบบ rollup ขึ้นเป็นระดับแผนกให้เอง

   Idempotent : YES (รันซ้ำได้)
   ต้องรันหลัง 26_kpi_feed_source.sql

   เหตุผลของการเปลี่ยน
   ---------------------------------------------------------------
   โจทย์ใหม่คือ "ดูได้ว่าใครในแผนกทำ KPI ครบแล้วบ้าง ใครยังไม่ครบ"
   ค่าระดับแผนกอย่างเดียวตอบไม่ได้ เพราะแผนกที่ค่าเฉลี่ยผ่านเกณฑ์
   อาจมีคนที่ยังไม่ได้เริ่มทำเลยซ่อนอยู่ข้างใน

   ทำไมยังเก็บ core.FactKpiMonthly ไว้
   ---------------------------------------------------------------
   ไม่ได้เก็บไว้เป็นแหล่งข้อมูลอิสระอีกต่อไป แต่เป็น "ผลรวมที่คำนวณจาก
   ระดับบุคคล" ผ่าน core.usp_Rollup_KpiEmployeeToDept ทำให้ Dashboard /
   Excel / PDF / อีเมล ที่อ่านตารางนี้อยู่แล้วทำงานต่อได้ทันที และตัวเลข
   สองระดับตรงกันเสมอโดยไม่ต้อง sync

   การคำนวณสถานะ
   ---------------------------------------------------------------
   ระบบต้นทางส่ง CompletionStatus มาเอง ถ้าไม่ส่ง ระบบจะอนุมานจาก
   ค่าเทียบเป้า (ทิศทาง H/L) ซึ่งไม่ใช่การคำนวณ KPI ใหม่ แต่เป็นการ
   แปลผลเพื่อนำเสนอ
   ============================================================= */

USE KpiMonthlyReport;
GO

/* ด่านตรวจ: ขาดตารางไหนบอกชื่อตารางนั้นไปเลย
   ---------------------------------------------------------------
   เมื่อด่านนี้ทำงาน SET NOEXEC ON จะทำให้ทุก batch ที่เหลือถูก "compile
   แต่ไม่ execute" ผลคือหน้าจอเต็มไปด้วย Invalid column name 'Scope' และ
   Invalid object name 'core.FactKpiEmployeeMonthly' ซึ่งเป็นอาการ ไม่ใช่สาเหตุ
   สาเหตุจริงคือข้อความ "หยุด:" ข้างล่างนี้บรรทัดเดียว                      */
DECLARE @missing NVARCHAR(500) = N'';

IF OBJECT_ID('meta.KpiDefinition')  IS NULL SET @missing += N'meta.KpiDefinition (รัน 02_meta_tables.sql) ';
IF OBJECT_ID('core.FactKpiMonthly') IS NULL SET @missing += N'core.FactKpiMonthly (รัน 04_core_tables.sql) ';
IF OBJECT_ID('core.usp_RefreshKpi_Derived') IS NULL SET @missing += N'core.usp_RefreshKpi_Derived (รัน 26_kpi_feed_source.sql) ';

IF @missing <> N''
BEGIN
    RAISERROR(N'หยุด: ยังไม่มี %s — ยังไม่มีอะไรถูกแก้ ให้รันไฟล์ที่วงเล็บบอกก่อนแล้วค่อยรันไฟล์นี้ใหม่', 16, 1, @missing);
    SET NOEXEC ON;
END
GO

PRINT '>> เริ่มเปลี่ยนเป็นโมเดล KPI ระดับบุคคล';
GO

/* =============================================================
   0) ทะเบียนพนักงาน

   core.DimEmployee เคยถูกสร้างโดย 15_hr_tables.sql ซึ่งเป็นยุคที่ KPI
   คำนวณจากข้อมูลลงเวลา แล้วถูก 26 ลบทิ้งไปพร้อมตารางลงเวลาทั้งชุด

   ตอนนี้ KPI วัดรายบุคคล ทะเบียนพนักงานจึงกลับมาเป็นของจำเป็น แต่ไม่ควร
   ให้ไปรัน 15 ซึ่งจะลากตารางลงเวลาที่เลิกใช้แล้ว (stg.AttendanceRaw,
   core.FactAttendance, core.DimAttendanceStatus) กลับมาด้วย
   ไฟล์นี้จึงสร้างเฉพาะสองตารางที่ยังใช้จริง

   นิยามเหมือน 15 ทุกประการ ฐานข้อมูลที่ยังไม่เคยรัน 26 จึงไม่โดนแก้อะไร
   ============================================================= */
IF OBJECT_ID('core.DimEmployee') IS NULL
BEGIN
CREATE TABLE core.DimEmployee
(
    EmployeeId      INT             IDENTITY(1,1) NOT NULL,
    EmployeeCode    VARCHAR(20)     NOT NULL,       -- EMP-0001
    EmployeeName    NVARCHAR(150)   NOT NULL,
    DepartmentId    INT             NOT NULL,
    Position        NVARCHAR(100)   NULL,
    HireDate        DATE            NULL,
    IsActive        BIT             NOT NULL CONSTRAINT DF_Emp_Active DEFAULT (1),
    CreatedAt       DATETIME2(0)    NOT NULL CONSTRAINT DF_Emp_Created DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_DimEmployee PRIMARY KEY CLUSTERED (EmployeeId),
    CONSTRAINT UQ_DimEmployee_Code UNIQUE (EmployeeCode),
    CONSTRAINT FK_DimEmployee_Dept FOREIGN KEY (DepartmentId)
        REFERENCES core.DimDepartment(DepartmentId)
);
PRINT '>> Created core.DimEmployee';
END
ELSE
    PRINT '>> core.DimEmployee already exists';
GO

-- แถว UNKNOWN สำหรับข้อมูลที่หาพนักงานไม่เจอ -> ไม่ทิ้งข้อมูลเงียบ ๆ
IF NOT EXISTS (SELECT 1 FROM core.DimEmployee WHERE EmployeeId = -1)
BEGIN
    SET IDENTITY_INSERT core.DimEmployee ON;
    INSERT INTO core.DimEmployee (EmployeeId, EmployeeCode, EmployeeName, DepartmentId, IsActive)
    VALUES (-1, 'UNKNOWN', N'Unknown Employee', -1, 0);
    SET IDENTITY_INSERT core.DimEmployee OFF;
END
GO

/* ตารางแปลงรหัสพนักงานที่ต้นทางสะกดไม่ตรง -> EmployeeId
   หลักการเดียวกับ core.DepartmentAlias */
IF OBJECT_ID('core.EmployeeAlias') IS NULL
BEGIN
CREATE TABLE core.EmployeeAlias
(
    AliasId         INT             IDENTITY(1,1) NOT NULL,
    AliasText       NVARCHAR(100)   NOT NULL,       -- normalize แล้ว
    EmployeeId      INT             NOT NULL,

    CONSTRAINT PK_EmployeeAlias PRIMARY KEY CLUSTERED (AliasId),
    CONSTRAINT UQ_EmployeeAlias_Text UNIQUE (AliasText),
    CONSTRAINT FK_EmployeeAlias_Emp FOREIGN KEY (EmployeeId)
        REFERENCES core.DimEmployee(EmployeeId)
);
PRINT '>> Created core.EmployeeAlias';
END
ELSE
    PRINT '>> core.EmployeeAlias already exists';
GO

/* =============================================================
   1) meta.KpiDefinition : แยกให้รู้ว่า KPI ตัวไหนวัดรายบุคคล
      และตัวไหนเป็นรายการเช็ก "ทำแล้ว/ยังไม่ทำ" ที่ไม่มีตัวเลข
   ============================================================= */
IF COL_LENGTH('meta.KpiDefinition', 'Scope') IS NULL
BEGIN
    ALTER TABLE meta.KpiDefinition ADD Scope VARCHAR(10) NOT NULL
        CONSTRAINT DF_Kpi_Scope DEFAULT ('EMPLOYEE');
    PRINT '>> เพิ่มคอลัมน์ meta.KpiDefinition.Scope';
END
GO

IF COL_LENGTH('meta.KpiDefinition', 'IsChecklist') IS NULL
BEGIN
    /* 1 = ไม่มีค่าตัวเลข วัดแค่ทำเสร็จหรือยัง (เช่น ส่งแบบประเมินตนเอง)
       0 = มีค่าตัวเลขเทียบเป้า (เช่น ชั่วโมงอบรม)                     */
    ALTER TABLE meta.KpiDefinition ADD IsChecklist BIT NOT NULL
        CONSTRAINT DF_Kpi_Checklist DEFAULT (0);
    PRINT '>> เพิ่มคอลัมน์ meta.KpiDefinition.IsChecklist';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_KpiDefinition_Scope')
    ALTER TABLE meta.KpiDefinition ADD CONSTRAINT CK_KpiDefinition_Scope
        CHECK (Scope IN ('EMPLOYEE','DEPARTMENT'));
GO

/* KPI ระดับแผนก 3 ตัวของเดิม (26) เลิกใช้
   ---------------------------------------------------------------
   ปิดด้วย IsActive = 0 ไม่ลบทิ้ง เพราะฐานที่มีข้อมูลเดิมอยู่แล้วจะยัง
   เปิดรายงานย้อนหลังได้ และย้อนกลับได้ด้วย UPDATE บรรทัดเดียว */
UPDATE meta.KpiDefinition
SET IsActive  = 0,
    UpdatedAt = SYSDATETIME(),
    UpdatedBy = N'MIGRATION_30'
WHERE KpiCode IN ('ATTENDANCE_RATE','OVERTIME_HRS','ABSENCE_RATE');
GO

/* รหัสต้นทางต้องปล่อยคืนให้ KPI ตัวใหม่ใช้ได้
   (UX_KpiDefinition_SourceMetric เป็น unique index) */
UPDATE meta.KpiDefinition
SET SourceMetricCode = NULL
WHERE IsActive = 0 AND SourceMetricCode IS NOT NULL;
GO

/* =============================================================
   2) นิยาม KPI รายบุคคล
      เพิ่ม KPI ตัวใหม่ในอนาคต = insert อีกแถวที่นี่ แล้วให้ต้นทางส่ง
      SourceMetricCode ตัวนั้นมาใน feed — ไม่ต้องแก้โค้ดใด ๆ
   ============================================================= */
MERGE meta.KpiDefinition AS t
USING (VALUES
    ('EMP_ATTENDANCE',  N'Attendance Rate',       N'อัตราการมางาน',            N'HR', N'%',     2, 'H', 0, 10),
    ('EMP_OVERTIME',    N'Overtime Hours',        N'ชั่วโมงล่วงเวลา',          N'HR', N'hrs',   1, 'L', 0, 20),
    ('EMP_TRAINING',    N'Training Hours',        N'ชั่วโมงอบรมพัฒนาตนเอง',    N'HR', N'hrs',   1, 'H', 0, 30),
    ('EMP_SAFETY',      N'Safety Course',         N'อบรมความปลอดภัยประจำเดือน', N'HR', N'',      0, 'H', 1, 40),
    ('EMP_KAIZEN',      N'Kaizen Submission',     N'ข้อเสนอปรับปรุงงาน',        N'HR', N'เรื่อง', 0, 'H', 0, 50),
    ('EMP_SELF_EVAL',   N'Self Evaluation',       N'ส่งแบบประเมินตนเอง',        N'HR', N'',      0, 'H', 1, 60)
) AS s (KpiCode, KpiName, KpiNameTh, CategoryName, Unit, DecimalPlaces, Direction, IsChecklist, SortOrder)
ON t.KpiCode = s.KpiCode
WHEN MATCHED THEN UPDATE SET
    t.KpiName = s.KpiName, t.KpiNameTh = s.KpiNameTh, t.CategoryName = s.CategoryName,
    t.Unit = s.Unit, t.DecimalPlaces = s.DecimalPlaces, t.Direction = s.Direction,
    t.IsChecklist = s.IsChecklist, t.SortOrder = s.SortOrder,
    t.Scope = 'EMPLOYEE', t.IsActive = 1, t.CalcProcName = NULL,
    t.SourceMetricCode = s.KpiCode,
    t.FormulaText = N'ดึงค่ารายบุคคลจากระบบ KPI ต้นทาง (ระบบนี้ไม่คำนวณซ้ำ)',
    t.UpdatedAt = SYSDATETIME(), t.UpdatedBy = N'MIGRATION_30'
WHEN NOT MATCHED THEN
    INSERT (KpiCode, KpiName, KpiNameTh, CategoryName, Unit, DecimalPlaces, Direction,
            CalcProcName, SourceMetricCode, FormulaText, SortOrder, Scope, IsChecklist, CreatedBy)
    VALUES (s.KpiCode, s.KpiName, s.KpiNameTh, s.CategoryName, s.Unit, s.DecimalPlaces,
            s.Direction, NULL, s.KpiCode,
            N'ดึงค่ารายบุคคลจากระบบ KPI ต้นทาง (ระบบนี้ไม่คำนวณซ้ำ)',
            s.SortOrder, 'EMPLOYEE', s.IsChecklist, N'MIGRATION_30');
GO

PRINT '>> นิยาม KPI รายบุคคล 6 ตัวพร้อมใช้';
GO

/* =============================================================
   3) core.FactKpiEmployeeMonthly
      1 แถว = พนักงาน 1 คน × KPI 1 ตัว × 1 เดือน

      DepartmentId เก็บซ้ำไว้ในตารางนี้ทั้งที่หาได้จาก DimEmployee
      เพราะเป็น "แผนกที่สังกัดตอนนั้น" ถ้าย้ายแผนกกลางปี รายงานเดือน
      เก่าต้องยังอยู่กับแผนกเดิม และทำให้ filter รายแผนกไม่ต้อง join
   ============================================================= */
IF OBJECT_ID('core.FactKpiEmployeeMonthly') IS NULL
BEGIN
CREATE TABLE core.FactKpiEmployeeMonthly
(
    EmpKpiFactKey    BIGINT         IDENTITY(1,1) NOT NULL,
    MonthKey         INT            NOT NULL,
    EmployeeId       INT            NOT NULL,
    DepartmentId     INT            NOT NULL,
    KpiId            INT            NOT NULL,

    TargetValue      DECIMAL(18,4)  NULL,
    ActualValue      DECIMAL(18,4)  NULL,

    -- DONE = ทำครบแล้ว / IN_PROGRESS = เริ่มแล้วยังไม่ถึงเป้า / NOT_STARTED = ยังไม่เริ่ม
    CompletionStatus VARCHAR(12)    NOT NULL CONSTRAINT DF_EmpKpi_Status DEFAULT ('NOT_STARTED'),
    IsComplete       AS (CASE WHEN CompletionStatus = 'DONE' THEN 1 ELSE 0 END) PERSISTED,

    AchievementPct   AS (CASE WHEN TargetValue IS NULL OR TargetValue = 0 THEN NULL
                              ELSE (ActualValue / NULLIF(TargetValue,0)) * 100 END),

    CompletedDate    DATE           NULL,
    Remark           NVARCHAR(300)  NULL,

    LoadedAt         DATETIME2(0)   NOT NULL CONSTRAINT DF_EmpKpi_At DEFAULT (SYSDATETIME()),
    SourceRunId      BIGINT         NULL,

    CONSTRAINT PK_FactKpiEmployeeMonthly PRIMARY KEY CLUSTERED (EmpKpiFactKey),
    CONSTRAINT UQ_FactKpiEmp_NK UNIQUE (MonthKey, EmployeeId, KpiId),
    CONSTRAINT FK_FactKpiEmp_Emp  FOREIGN KEY (EmployeeId)   REFERENCES core.DimEmployee(EmployeeId),
    CONSTRAINT FK_FactKpiEmp_Dept FOREIGN KEY (DepartmentId) REFERENCES core.DimDepartment(DepartmentId),
    CONSTRAINT FK_FactKpiEmp_Kpi  FOREIGN KEY (KpiId)        REFERENCES meta.KpiDefinition(KpiId),
    CONSTRAINT CK_FactKpiEmp_Status CHECK (CompletionStatus IN ('DONE','IN_PROGRESS','NOT_STARTED'))
);
CREATE INDEX IX_FactKpiEmp_MonthDept ON core.FactKpiEmployeeMonthly(MonthKey, DepartmentId)
    INCLUDE (EmployeeId, KpiId, CompletionStatus, ActualValue, TargetValue);
CREATE INDEX IX_FactKpiEmp_Employee  ON core.FactKpiEmployeeMonthly(EmployeeId, MonthKey);
PRINT '>> Created core.FactKpiEmployeeMonthly';
END
ELSE
    PRINT '>> core.FactKpiEmployeeMonthly already exists';
GO

/* =============================================================
   4) stg.KpiEmployeeFeedRaw  <- ข้อมูลดิบรายบุคคลจากระบบต้นทาง
      ทุกคอลัมน์ NVARCHAR ตามหลักการชั้น stg เดิม:
      ข้อมูลเพี้ยนหนึ่งแถวต้องไม่ทำให้การ import ทั้งรอบล้ม
   ============================================================= */
IF OBJECT_ID('stg.KpiEmployeeFeedRaw') IS NULL
BEGIN
CREATE TABLE stg.KpiEmployeeFeedRaw
(
    StgId             BIGINT        IDENTITY(1,1) NOT NULL,
    RunId             BIGINT        NULL,
    LoadedAt          DATETIME2(0)  NOT NULL CONSTRAINT DF_StgEmpFeed_At DEFAULT (SYSDATETIME()),
    SourceName        NVARCHAR(260) NULL,
    SourceLineNo      INT           NULL,

    MonthText         NVARCHAR(50)  NULL,   -- 202601 / 2026-01
    EmployeeCodeText  NVARCHAR(50)  NULL,   -- EMP-0001
    EmployeeNameText  NVARCHAR(150) NULL,   -- ใช้ตอน reject log เท่านั้น ไม่เชื่อชื่อเป็นคีย์
    DepartmentText    NVARCHAR(100) NULL,
    KpiCodeText       NVARCHAR(50)  NULL,
    TargetValueText   NVARCHAR(50)  NULL,
    ActualValueText   NVARCHAR(50)  NULL,
    StatusText        NVARCHAR(30)  NULL,   -- DONE / IN_PROGRESS / NOT_STARTED (ว่างได้)
    CompletedDateText NVARCHAR(50)  NULL,

    IsProcessed       BIT           NOT NULL CONSTRAINT DF_StgEmpFeed_Proc DEFAULT (0),

    CONSTRAINT PK_KpiEmployeeFeedRaw PRIMARY KEY CLUSTERED (StgId)
);
CREATE INDEX IX_KpiEmpFeedRaw_Run ON stg.KpiEmployeeFeedRaw(RunId, IsProcessed);
PRINT '>> Created stg.KpiEmployeeFeedRaw';
END
ELSE
    PRINT '>> stg.KpiEmployeeFeedRaw already exists';
GO

/* =============================================================
   5) core.usp_Transform_KpiEmployeeFeed
      stg.KpiEmployeeFeedRaw -> core.FactKpiEmployeeMonthly

      หลักการเดียวกับ core.usp_Transform_KpiFeed ของ 26:
        - parse ทุกอย่างก่อน แล้วค่อยจำแนกว่าแถวไหนรับไม่ได้
        - แถวเสียเข้า meta.DataRejectLog พร้อมเหตุผล ไม่ทำทั้งรอบล้ม
        - purge ก่อน insert เฉพาะเดือนที่ feed ส่งมา -> รันซ้ำได้
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_Transform_KpiEmployeeFeed
    @RunId        BIGINT,
    @RowsWritten  INT OUTPUT,
    @RowsRejected INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @RowsWritten  = 0;
    SET @RowsRejected = 0;

    BEGIN TRY
        BEGIN TRAN;

        /* ---------- 1) PARSE + resolve dimension ---------- */
        IF OBJECT_ID('tempdb..#empfeed') IS NOT NULL DROP TABLE #empfeed;

        SELECT
            r.StgId,
            TRY_CONVERT(INT, REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(r.MonthText, N''))), '-', ''), '/', ''))
                                                          AS MonthKey,
            COALESCE(e.EmployeeId, ea.EmployeeId)         AS EmployeeId,
            /* แผนกยึดจากทะเบียนพนักงานเป็นหลัก ที่ feed ส่งมาใช้เป็นตัวสำรอง
               เพราะทะเบียนคือแหล่งความจริงของการสังกัด ไม่ใช่ไฟล์ที่ส่งมา */
            COALESCE(e.DepartmentId, ea2.DepartmentId, d.DepartmentId, al.DepartmentId)
                                                          AS DepartmentId,
            k.KpiId,
            k.Direction,
            k.IsChecklist,
            core.fn_ParseDecimal(r.TargetValueText)       AS TargetValue,
            core.fn_ParseDecimal(r.ActualValueText)       AS ActualValue,
            UPPER(LTRIM(RTRIM(ISNULL(r.StatusText, N'')))) AS StatusText,
            core.fn_ParseDate(r.CompletedDateText)        AS CompletedDate,
            r.MonthText         AS RawMonth,
            r.EmployeeCodeText  AS RawEmp,
            r.DepartmentText    AS RawDept,
            r.KpiCodeText       AS RawKpi,
            r.ActualValueText   AS RawActual
        INTO #empfeed
        FROM stg.KpiEmployeeFeedRaw r
        LEFT JOIN core.DimEmployee e
               ON core.fn_NormalizeText(e.EmployeeCode) = core.fn_NormalizeText(r.EmployeeCodeText)
              AND e.EmployeeId > 0
        LEFT JOIN core.EmployeeAlias ea
               ON ea.AliasText = core.fn_NormalizeText(r.EmployeeCodeText)
        LEFT JOIN core.DimEmployee ea2
               ON ea2.EmployeeId = ea.EmployeeId
        LEFT JOIN core.DimDepartment d
               ON core.fn_NormalizeText(d.DepartmentCode) = core.fn_NormalizeText(r.DepartmentText)
              AND d.DepartmentId > 0
        LEFT JOIN core.DepartmentAlias al
               ON al.AliasText = core.fn_NormalizeText(r.DepartmentText)
        LEFT JOIN meta.KpiDefinition k
               ON core.fn_NormalizeText(k.SourceMetricCode) = core.fn_NormalizeText(r.KpiCodeText)
              AND k.IsActive = 1
              AND k.Scope = 'EMPLOYEE'
        WHERE r.RunId = @RunId;

        /* ---------- 2) เติมสถานะที่ต้นทางไม่ได้ส่งมา ----------
           ไม่ใช่การคำนวณ KPI ใหม่ แต่เป็นการแปลผลค่าที่ได้รับมาแล้ว
             checklist : มีค่า > 0 = ทำแล้ว
             มีตัวเลข  : ถึงเป้าตามทิศทาง H/L = DONE
                         มีค่าแต่ยังไม่ถึงเป้า  = IN_PROGRESS
                         ไม่มีค่า/เป็นศูนย์     = NOT_STARTED               */
        IF OBJECT_ID('tempdb..#empstatus') IS NOT NULL DROP TABLE #empstatus;

        SELECT f.*,
            CASE
                WHEN f.StatusText IN ('DONE','COMPLETED','COMPLETE','FINISHED','Y','YES','1')
                    THEN 'DONE'
                WHEN f.StatusText IN ('IN_PROGRESS','INPROGRESS','PARTIAL','WIP')
                    THEN 'IN_PROGRESS'
                WHEN f.StatusText IN ('NOT_STARTED','NOTSTARTED','NONE','N','NO','0')
                    THEN 'NOT_STARTED'
                WHEN f.StatusText <> '' THEN NULL          -- ส่งค่าที่ไม่รู้จักมา = ต้องถูก reject
                WHEN f.ActualValue IS NULL THEN 'NOT_STARTED'
                WHEN f.IsChecklist = 1
                    THEN CASE WHEN f.ActualValue > 0 THEN 'DONE' ELSE 'NOT_STARTED' END
                WHEN f.TargetValue IS NULL THEN 'IN_PROGRESS'
                WHEN (f.Direction = 'H' AND f.ActualValue >= f.TargetValue)
                  OR (f.Direction = 'L' AND f.ActualValue <= f.TargetValue)
                    THEN 'DONE'
                WHEN f.ActualValue = 0 AND f.Direction = 'H' THEN 'NOT_STARTED'
                ELSE 'IN_PROGRESS'
            END AS CompletionStatus
        INTO #empstatus
        FROM #empfeed f;

        /* ---------- 3) จำแนกแถวที่รับไม่ได้ ---------- */
        IF OBJECT_ID('tempdb..#empclass') IS NOT NULL DROP TABLE #empclass;

        SELECT s.*,
            CASE
                WHEN s.MonthKey IS NULL
                  OR s.MonthKey < 190001 OR s.MonthKey > 299912        THEN 'INVALID_MONTH'
                WHEN NOT EXISTS (SELECT 1 FROM core.DimDate dd
                                 WHERE dd.MonthKey = s.MonthKey)       THEN 'MONTH_OUT_OF_CALENDAR'
                WHEN s.EmployeeId IS NULL                              THEN 'UNKNOWN_EMPLOYEE'
                WHEN s.DepartmentId IS NULL                            THEN 'UNKNOWN_DEPARTMENT'
                WHEN s.KpiId IS NULL                                   THEN 'UNKNOWN_KPI'
                WHEN s.CompletionStatus IS NULL                        THEN 'UNKNOWN_STATUS'
                ELSE NULL
            END AS RejectReason
        INTO #empclass
        FROM #empstatus s;

        INSERT INTO meta.DataRejectLog (RunId, SourceTable, SourceRowId, RejectReason, RawPayload)
        SELECT @RunId, 'stg.KpiEmployeeFeedRaw', c.StgId, c.RejectReason,
            CONCAT('{"Month":"',    STRING_ESCAPE(ISNULL(c.RawMonth,  ''), 'json'),
                   '","Employee":"',STRING_ESCAPE(ISNULL(c.RawEmp,    ''), 'json'),
                   '","Dept":"',    STRING_ESCAPE(ISNULL(c.RawDept,   ''), 'json'),
                   '","Kpi":"',     STRING_ESCAPE(ISNULL(c.RawKpi,    ''), 'json'),
                   '","Actual":"',  STRING_ESCAPE(ISNULL(c.RawActual, ''), 'json'), '"}')
        FROM #empclass c
        WHERE c.RejectReason IS NOT NULL;

        SET @RowsRejected = @@ROWCOUNT;

        /* ---------- 4) ตัดซ้ำ: เดือน+คน+KPI มีได้ค่าเดียว ----------
           ต้นทางส่งค่าแก้ย้อนหลังมาได้ จึงยึดแถวหลังสุดเป็นของจริง */
        IF OBJECT_ID('tempdb..#empfinal') IS NOT NULL DROP TABLE #empfinal;

        SELECT MonthKey, EmployeeId, DepartmentId, KpiId,
               TargetValue, ActualValue, CompletionStatus, CompletedDate
        INTO #empfinal
        FROM (
            SELECT c.*,
                   ROW_NUMBER() OVER (
                       PARTITION BY c.MonthKey, c.EmployeeId, c.KpiId
                       ORDER BY c.StgId DESC
                   ) AS rn
            FROM #empclass c
            WHERE c.RejectReason IS NULL
        ) t
        WHERE t.rn = 1;

        /* ---------- 5) purge เฉพาะเดือนที่ feed รอบนี้ส่งมา ----------
           ทำให้รันไฟล์เดือนเดิมซ้ำได้โดยไม่เกิดข้อมูลซ้อน และไม่ไปแตะเดือนอื่น */
        DELETE f
        FROM core.FactKpiEmployeeMonthly f
        WHERE EXISTS (SELECT 1 FROM #empfinal n WHERE n.MonthKey = f.MonthKey);

        INSERT INTO core.FactKpiEmployeeMonthly
            (MonthKey, EmployeeId, DepartmentId, KpiId,
             TargetValue, ActualValue, CompletionStatus, CompletedDate, SourceRunId)
        SELECT MonthKey, EmployeeId, DepartmentId, KpiId,
               TargetValue, ActualValue, CompletionStatus, CompletedDate, @RunId
        FROM #empfinal;

        SET @RowsWritten = @@ROWCOUNT;

        UPDATE stg.KpiEmployeeFeedRaw SET IsProcessed = 1 WHERE RunId = @RunId;

        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO
PRINT '>> Created core.usp_Transform_KpiEmployeeFeed';
GO

/* =============================================================
   6) KPI สรุประดับแผนก : "ร้อยละความสำเร็จของ KPI ทั้งแผนก"
      ตัวนี้ไม่ได้มาจาก feed แต่คำนวณจากข้อมูลรายบุคคล จึงเป็น
      Scope = DEPARTMENT และไม่มี SourceMetricCode
   ============================================================= */
MERGE meta.KpiDefinition AS t
USING (VALUES
    ('DEPT_KPI_COMPLETION', N'KPI Completion Rate', N'ร้อยละ KPI ที่ทำสำเร็จ',
     N'HR', N'%', 1, 'H', 5)
) AS s (KpiCode, KpiName, KpiNameTh, CategoryName, Unit, DecimalPlaces, Direction, SortOrder)
ON t.KpiCode = s.KpiCode
WHEN MATCHED THEN UPDATE SET
    t.KpiName = s.KpiName, t.KpiNameTh = s.KpiNameTh, t.Unit = s.Unit,
    t.DecimalPlaces = s.DecimalPlaces, t.Direction = s.Direction, t.SortOrder = s.SortOrder,
    t.Scope = 'DEPARTMENT', t.IsChecklist = 0, t.IsActive = 1,
    t.CalcProcName = NULL, t.SourceMetricCode = NULL,
    t.FormulaText = N'(จำนวน KPI รายบุคคลที่สถานะ DONE ÷ จำนวน KPI รายบุคคลทั้งหมดในแผนก) × 100',
    t.UpdatedAt = SYSDATETIME(), t.UpdatedBy = N'MIGRATION_30'
WHEN NOT MATCHED THEN
    INSERT (KpiCode, KpiName, KpiNameTh, CategoryName, Unit, DecimalPlaces, Direction,
            CalcProcName, SourceMetricCode, FormulaText, SortOrder, Scope, IsChecklist, CreatedBy)
    VALUES (s.KpiCode, s.KpiName, s.KpiNameTh, s.CategoryName, s.Unit, s.DecimalPlaces,
            s.Direction, NULL, NULL,
            N'(จำนวน KPI รายบุคคลที่สถานะ DONE ÷ จำนวน KPI รายบุคคลทั้งหมดในแผนก) × 100',
            s.SortOrder, 'DEPARTMENT', 0, N'MIGRATION_30');
GO

/* =============================================================
   7) core.usp_Rollup_KpiEmployeeToDept
      รายบุคคล -> core.FactKpiMonthly (ระดับแผนก)

      ตารางระดับแผนกไม่ใช่แหล่งข้อมูลอิสระอีกต่อไป แต่เป็นผลรวมที่
      สร้างใหม่ได้เสมอ Dashboard / Excel / PDF / อีเมล จึงใช้ของเดิมต่อได้

      วิธีรวม
        KPI แบบเช็ก  -> % ของคนที่ทำเสร็จ (เป้า = 100)
        KPI มีตัวเลข -> ค่าเฉลี่ยของทั้งแผนก (เป้า = ค่าเฉลี่ยของเป้ารายคน)
      ค่าเฉลี่ยเหมาะกับ KPI รายบุคคลมากกว่าผลรวม เพราะแผนกใหญ่ไม่ควร
      ได้เปรียบเพียงเพราะมีคนเยอะ

      @MonthKey NULL = ทำใหม่ทุกเดือนที่มีข้อมูล
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_Rollup_KpiEmployeeToDept
    @MonthKey INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @CompletionKpiId INT =
        (SELECT KpiId FROM meta.KpiDefinition WHERE KpiCode = 'DEPT_KPI_COMPLETION');

    BEGIN TRY
        BEGIN TRAN;

        IF OBJECT_ID('tempdb..#roll') IS NOT NULL DROP TABLE #roll;

        /* ---- KPI รายตัว ---- */
        SELECT
            f.MonthKey,
            f.KpiId,
            f.DepartmentId,
            CASE WHEN k.IsChecklist = 1
                 THEN 100.0 * SUM(CAST(f.IsComplete AS DECIMAL(18,4))) / NULLIF(COUNT(*), 0)
                 ELSE AVG(f.ActualValue)
            END                                        AS ActualValue,
            CASE WHEN k.IsChecklist = 1 THEN 100.0
                 ELSE AVG(f.TargetValue)
            END                                        AS TargetValue,
            SUM(CAST(f.IsComplete AS DECIMAL(18,4)))   AS Numerator,
            CAST(COUNT(*) AS DECIMAL(18,4))            AS Denominator
        INTO #roll
        FROM core.FactKpiEmployeeMonthly f
        JOIN meta.KpiDefinition k ON k.KpiId = f.KpiId
        WHERE (@MonthKey IS NULL OR f.MonthKey = @MonthKey)
        GROUP BY f.MonthKey, f.KpiId, f.DepartmentId, k.IsChecklist;

        /* ---- KPI สรุปรวมของแผนก ---- */
        IF @CompletionKpiId IS NOT NULL
        INSERT INTO #roll (MonthKey, KpiId, DepartmentId, ActualValue, TargetValue, Numerator, Denominator)
        SELECT f.MonthKey, @CompletionKpiId, f.DepartmentId,
               100.0 * SUM(CAST(f.IsComplete AS DECIMAL(18,4))) / NULLIF(COUNT(*), 0),
               100.0,
               SUM(CAST(f.IsComplete AS DECIMAL(18,4))),
               CAST(COUNT(*) AS DECIMAL(18,4))
        FROM core.FactKpiEmployeeMonthly f
        WHERE (@MonthKey IS NULL OR f.MonthKey = @MonthKey)
        GROUP BY f.MonthKey, f.DepartmentId;

        /* ลบเฉพาะเดือน/แผนกที่กำลังสร้างใหม่ แล้วค่อย insert — รันซ้ำได้ */
        DELETE t
        FROM core.FactKpiMonthly t
        WHERE EXISTS (SELECT 1 FROM #roll r
                      WHERE r.MonthKey = t.MonthKey AND r.KpiId = t.KpiId
                        AND r.DepartmentId = t.DepartmentId);

        INSERT INTO core.FactKpiMonthly
            (MonthKey, KpiId, DepartmentId, ActualValue, TargetValue, Numerator, Denominator)
        SELECT MonthKey, KpiId, DepartmentId, ActualValue, TargetValue, Numerator, Denominator
        FROM #roll;

        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH

    /* PrevMonthValue + StatusFlag (ของ 26) ต้องทำหลัง insert เสมอ */
    EXEC core.usp_RefreshKpi_Derived @FromMonthKey = @MonthKey;
END
GO
PRINT '>> Created core.usp_Rollup_KpiEmployeeToDept';
GO

/* =============================================================
   8) View สำหรับหน้า Monitoring
      ใส่ชื่อคน/ชื่อแผนก/ชื่อ KPI มาให้พร้อม เว็บจะได้ไม่ต้อง join เอง
   ============================================================= */
CREATE OR ALTER VIEW rpt.vw_EmployeeKpiStatus
AS
SELECT
    f.MonthKey,
    f.EmployeeId,
    e.EmployeeCode,
    e.EmployeeName,
    e.Position,
    f.DepartmentId,
    d.DepartmentCode,
    d.DepartmentName,
    d.DepartmentNameTh,
    f.KpiId,
    k.KpiCode,
    k.KpiName,
    k.KpiNameTh,
    k.Unit,
    k.DecimalPlaces,
    k.Direction,
    k.IsChecklist,
    k.SortOrder,
    f.TargetValue,
    f.ActualValue,
    f.AchievementPct,
    f.CompletionStatus,
    f.IsComplete,
    f.CompletedDate
FROM core.FactKpiEmployeeMonthly f
JOIN core.DimEmployee    e ON e.EmployeeId   = f.EmployeeId
JOIN core.DimDepartment  d ON d.DepartmentId = f.DepartmentId
JOIN meta.KpiDefinition  k ON k.KpiId        = f.KpiId;
GO

/* 1 แถว = พนักงาน 1 คนในเดือนหนึ่ง — ทำ KPI ไปถึงไหนแล้ว */
CREATE OR ALTER VIEW rpt.vw_EmployeeKpiSummary
AS
SELECT
    f.MonthKey,
    f.EmployeeId,
    e.EmployeeCode,
    e.EmployeeName,
    e.Position,
    f.DepartmentId,
    d.DepartmentCode,
    d.DepartmentName,
    COUNT(*)                                                       AS TotalKpi,
    SUM(CASE WHEN f.CompletionStatus = 'DONE'        THEN 1 ELSE 0 END) AS DoneCount,
    SUM(CASE WHEN f.CompletionStatus = 'IN_PROGRESS' THEN 1 ELSE 0 END) AS InProgressCount,
    SUM(CASE WHEN f.CompletionStatus = 'NOT_STARTED' THEN 1 ELSE 0 END) AS NotStartedCount,
    CAST(100.0 * SUM(CASE WHEN f.CompletionStatus = 'DONE' THEN 1 ELSE 0 END)
         / NULLIF(COUNT(*), 0) AS DECIMAL(5,1))                    AS CompletionPct,
    CAST(CASE WHEN SUM(CASE WHEN f.CompletionStatus = 'DONE' THEN 0 ELSE 1 END) = 0
              THEN 1 ELSE 0 END AS BIT)                            AS IsFullyComplete
FROM core.FactKpiEmployeeMonthly f
JOIN core.DimEmployee   e ON e.EmployeeId   = f.EmployeeId
JOIN core.DimDepartment d ON d.DepartmentId = f.DepartmentId
GROUP BY f.MonthKey, f.EmployeeId, e.EmployeeCode, e.EmployeeName, e.Position,
         f.DepartmentId, d.DepartmentCode, d.DepartmentName;
GO

/* 1 แถว = แผนก 1 แผนกในเดือนหนึ่ง — ภาพรวมของหน้า Monitoring */
CREATE OR ALTER VIEW rpt.vw_DepartmentKpiCompletion
AS
SELECT
    s.MonthKey,
    s.DepartmentId,
    s.DepartmentCode,
    s.DepartmentName,
    COUNT(*)                                                        AS EmployeeCount,
    SUM(CASE WHEN s.IsFullyComplete = 1 THEN 1 ELSE 0 END)          AS EmployeeCompleteCount,
    SUM(CASE WHEN s.IsFullyComplete = 1 THEN 0 ELSE 1 END)          AS EmployeeIncompleteCount,
    SUM(s.TotalKpi)                                                 AS TotalKpi,
    SUM(s.DoneCount)                                                AS DoneKpi,
    SUM(s.InProgressCount)                                          AS InProgressKpi,
    SUM(s.NotStartedCount)                                          AS NotStartedKpi,
    CAST(100.0 * SUM(s.DoneCount) / NULLIF(SUM(s.TotalKpi), 0) AS DECIMAL(5,1))
                                                                    AS KpiCompletionPct,
    CAST(100.0 * SUM(CASE WHEN s.IsFullyComplete = 1 THEN 1 ELSE 0 END)
         / NULLIF(COUNT(*), 0) AS DECIMAL(5,1))                     AS EmployeeCompletionPct
FROM rpt.vw_EmployeeKpiSummary s
GROUP BY s.MonthKey, s.DepartmentId, s.DepartmentCode, s.DepartmentName;
GO

PRINT '>> Created rpt.vw_EmployeeKpiStatus / vw_EmployeeKpiSummary / vw_DepartmentKpiCompletion';
GO

/* =============================================================
   9) Stored procedure ที่เว็บและ ETL เรียก
      @DepartmentIds = รายการ id คั่นด้วย comma เช่น '1,3,5'
      NULL หรือว่าง = ทุกแผนก

      ใช้ STRING_SPLIT แทนการต่อสตริงเป็น SQL แล้ว EXEC
      เพราะการต่อสตริงคือช่องทาง SQL injection ตรง ๆ
   ============================================================= */
CREATE OR ALTER PROCEDURE rpt.usp_GetDepartmentMonitoring
    @MonthKey      INT,
    @DepartmentIds NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT c.*
    FROM rpt.vw_DepartmentKpiCompletion c
    WHERE c.MonthKey = @MonthKey
      AND (@DepartmentIds IS NULL OR LTRIM(RTRIM(@DepartmentIds)) = N''
           OR c.DepartmentId IN (SELECT TRY_CONVERT(INT, value)
                                 FROM STRING_SPLIT(@DepartmentIds, ',')))
    ORDER BY c.KpiCompletionPct ASC, c.DepartmentCode;   -- แผนกที่แย่สุดอยู่บนสุด
END
GO

CREATE OR ALTER PROCEDURE rpt.usp_GetEmployeeKpiStatus
    @MonthKey      INT,
    @DepartmentIds NVARCHAR(MAX) = NULL,
    @EmployeeId    INT           = NULL,
    @OnlyIncomplete BIT          = 0
AS
BEGIN
    SET NOCOUNT ON;

    SELECT s.*
    FROM rpt.vw_EmployeeKpiStatus s
    WHERE s.MonthKey = @MonthKey
      AND (@EmployeeId IS NULL OR s.EmployeeId = @EmployeeId)
      AND (@DepartmentIds IS NULL OR LTRIM(RTRIM(@DepartmentIds)) = N''
           OR s.DepartmentId IN (SELECT TRY_CONVERT(INT, value)
                                 FROM STRING_SPLIT(@DepartmentIds, ',')))
      AND (@OnlyIncomplete = 0 OR s.CompletionStatus <> 'DONE')
    ORDER BY s.DepartmentCode, s.EmployeeCode, s.SortOrder;
END
GO

CREATE OR ALTER PROCEDURE rpt.usp_GetEmployeeKpiSummary
    @MonthKey      INT,
    @DepartmentIds NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT s.*
    FROM rpt.vw_EmployeeKpiSummary s
    WHERE s.MonthKey = @MonthKey
      AND (@DepartmentIds IS NULL OR LTRIM(RTRIM(@DepartmentIds)) = N''
           OR s.DepartmentId IN (SELECT TRY_CONVERT(INT, value)
                                 FROM STRING_SPLIT(@DepartmentIds, ',')))
    ORDER BY s.DepartmentCode, s.CompletionPct ASC, s.EmployeeCode;
END
GO

PRINT '>> Created rpt.usp_GetDepartmentMonitoring / usp_GetEmployeeKpiStatus / usp_GetEmployeeKpiSummary';
GO

/* =============================================================
   10) rpt.vw_ValidMonth : เดือนที่มีข้อมูลจริง
       ย้ายฐานการตัดสินจาก core.FactKpiMonthly (26) มาที่ข้อมูลรายบุคคล
       ซึ่งเป็นแหล่งจริงแล้ว

       เกณฑ์: เดือนนั้นต้องมีพนักงานส่ง KPI มาอย่างน้อย 50% ของ
       จำนวนพนักงานเฉลี่ยต่อเดือน — เดือนที่ feed หลุดมาไม่กี่คน
       (เดือนผี) จะถูกกรองออกไม่ให้ขึ้นรายงาน
   ============================================================= */
CREATE OR ALTER VIEW rpt.vw_ValidMonth
AS
WITH emp_per_month AS (
    SELECT MonthKey, COUNT(DISTINCT EmployeeId) AS EmployeeCount
    FROM core.FactKpiEmployeeMonthly
    GROUP BY MonthKey
),
threshold AS (
    SELECT 0.50 * AVG(CAST(EmployeeCount AS FLOAT)) AS MinEmployees
    FROM emp_per_month
)
SELECT e.MonthKey
FROM emp_per_month e
CROSS JOIN threshold t
WHERE e.EmployeeCount >= t.MinEmployees;
GO

/* =============================================================
   11) core.usp_PurgeMonth : ต้องล้างข้อมูลรายบุคคลด้วย
       ไม่งั้นล้างเดือนแล้ว rollup รอบถัดไปจะคืนค่าเดิมกลับมา
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_PurgeMonth
    @MonthKey INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;
        DELETE FROM core.FactKpiEmployeeMonthly WHERE MonthKey = @MonthKey;
        DELETE FROM core.FactKpiMonthly         WHERE MonthKey = @MonthKey;
    COMMIT;

    PRINT CONCAT('>> ล้างข้อมูลเดือน ', @MonthKey, ' เรียบร้อย');
END
GO

PRINT '>> 30_employee_kpi_model.sql เสร็จสมบูรณ์';
GO

/* ปลด NOEXEC เสมอ ไม่ว่าจะจบแบบไหน
   ---------------------------------------------------------------
   SSMS ใช้ connection เดิมต่อในหน้าต่างเดียวกัน ถ้าด่านตรวจด้านบนทำงาน
   แล้วไม่ปลดตรงนี้ สคริปต์ถัดไปที่รันในหน้าต่างเดิมจะ "เหมือนรันผ่าน"
   ทั้งที่ไม่มีอะไรเกิดขึ้นจริงสักอย่าง ซึ่งหาสาเหตุยากกว่า error เสียอีก */
SET NOEXEC OFF;
GO
