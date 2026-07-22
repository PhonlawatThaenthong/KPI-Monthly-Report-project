/* =============================================================
   02_meta_tables.sql
   Purpose : ตาราง Metadata - KPI Definition, Target, ETL Log, Audit Log
   Idempotent : YES
   ============================================================= */

USE KpiMonthlyReport;
GO

/* -------------------------------------------------------------
   meta.KpiDefinition
   หัวใจของระบบ - สูตร KPI เก็บใน DB ไม่ hard-code ในโค้ด
   ------------------------------------------------------------- */
IF OBJECT_ID('meta.KpiDefinition') IS NULL
BEGIN
CREATE TABLE meta.KpiDefinition
(
    KpiId           INT             IDENTITY(1,1) NOT NULL,
    KpiCode         VARCHAR(30)     NOT NULL,   -- PROD_OUTPUT, DEFECT_RATE ...
    KpiName         NVARCHAR(150)   NOT NULL,
    KpiNameTh       NVARCHAR(150)   NULL,
    CategoryName    NVARCHAR(50)    NOT NULL,   -- Production / Quality / Cost
    Unit            NVARCHAR(20)    NOT NULL,   -- pcs, %, hrs, THB
    DecimalPlaces   TINYINT         NOT NULL CONSTRAINT DF_Kpi_Dec DEFAULT (2),

    -- ทิศทางที่ถือว่า "ดี" : H = ยิ่งมากยิ่งดี, L = ยิ่งน้อยยิ่งดี
    Direction       CHAR(1)         NOT NULL CONSTRAINT DF_Kpi_Dir DEFAULT ('H'),

    -- ชื่อ stored procedure ที่ใช้คำนวณ KPI ตัวนี้
    -- *** ห้ามเก็บ SQL expression แล้วเอาไปต่อสตริงรัน = SQL Injection ***
    CalcProcName    SYSNAME         NOT NULL,

    FormulaText     NVARCHAR(300)   NULL,       -- ไว้แสดงบนหน้าจอให้ user อ่าน
    SortOrder       INT             NOT NULL CONSTRAINT DF_Kpi_Sort DEFAULT (0),
    IsActive        BIT             NOT NULL CONSTRAINT DF_Kpi_Active DEFAULT (1),
    CreatedAt       DATETIME2(0)    NOT NULL CONSTRAINT DF_Kpi_Created DEFAULT (SYSDATETIME()),
    CreatedBy       NVARCHAR(128)   NULL,
    UpdatedAt       DATETIME2(0)    NULL,
    UpdatedBy       NVARCHAR(128)   NULL,

    CONSTRAINT PK_KpiDefinition PRIMARY KEY CLUSTERED (KpiId),
    CONSTRAINT UQ_KpiDefinition_Code UNIQUE (KpiCode),
    CONSTRAINT CK_KpiDefinition_Dir CHECK (Direction IN ('H','L'))
);
PRINT '>> Created meta.KpiDefinition';
END
GO

/* -------------------------------------------------------------
   meta.KpiTarget
   เป้าหมาย + baseline แยกตามเดือน/แผนก
   DepartmentId NULL = ใช้กับทุกแผนก
   ------------------------------------------------------------- */
IF OBJECT_ID('meta.KpiTarget') IS NULL
BEGIN
CREATE TABLE meta.KpiTarget
(
    KpiTargetId     INT             IDENTITY(1,1) NOT NULL,
    KpiId           INT             NOT NULL,
    MonthKey        INT             NOT NULL,   -- 202601
    DepartmentId    INT             NULL,
    TargetValue     DECIMAL(18,4)   NULL,
    BaselineValue   DECIMAL(18,4)   NULL,       -- ใช้คำนวณ Cost Down %
    Remark          NVARCHAR(200)   NULL,
    CreatedAt       DATETIME2(0)    NOT NULL CONSTRAINT DF_KpiTgt_Created DEFAULT (SYSDATETIME()),
    CreatedBy       NVARCHAR(128)   NULL,

    CONSTRAINT PK_KpiTarget PRIMARY KEY CLUSTERED (KpiTargetId),
    CONSTRAINT FK_KpiTarget_Kpi FOREIGN KEY (KpiId) REFERENCES meta.KpiDefinition(KpiId)
);

-- กันตั้ง target ซ้ำ (NULL department ต้องกันด้วย จึงใช้ filtered index 2 ชุด)
CREATE UNIQUE INDEX UX_KpiTarget_Dept
    ON meta.KpiTarget(KpiId, MonthKey, DepartmentId)
    WHERE DepartmentId IS NOT NULL;

CREATE UNIQUE INDEX UX_KpiTarget_All
    ON meta.KpiTarget(KpiId, MonthKey)
    WHERE DepartmentId IS NULL;

PRINT '>> Created meta.KpiTarget';
END
GO

/* -------------------------------------------------------------
   meta.EtlRunLog  -  1 แถวต่อ 1 รอบการรัน ETL
   ------------------------------------------------------------- */
IF OBJECT_ID('meta.EtlRunLog') IS NULL
BEGIN
CREATE TABLE meta.EtlRunLog
(
    RunId           BIGINT          IDENTITY(1,1) NOT NULL,
    JobName         NVARCHAR(100)   NOT NULL,   -- ETL_Monthly_Full
    MonthKey        INT             NULL,       -- รอบเดือนที่ประมวลผล
    StartedAt       DATETIME2(0)    NOT NULL CONSTRAINT DF_Etl_Start DEFAULT (SYSDATETIME()),
    FinishedAt      DATETIME2(0)    NULL,
    DurationSec     AS DATEDIFF(SECOND, StartedAt, FinishedAt) PERSISTED,
    Status          VARCHAR(10)     NOT NULL CONSTRAINT DF_Etl_Status DEFAULT ('RUNNING'),
    RowsRead        INT             NULL,
    RowsWritten     INT             NULL,
    RowsRejected    INT             NULL,
    TriggeredBy     NVARCHAR(128)   NULL,       -- SCHEDULER / ชื่อ user
    MachineName     NVARCHAR(100)   NULL,
    ErrorMessage    NVARCHAR(MAX)   NULL,

    CONSTRAINT PK_EtlRunLog PRIMARY KEY CLUSTERED (RunId),
    CONSTRAINT CK_EtlRunLog_Status CHECK (Status IN ('RUNNING','SUCCESS','FAILED','WARNING'))
);
CREATE INDEX IX_EtlRunLog_Job ON meta.EtlRunLog(JobName, StartedAt DESC);
PRINT '>> Created meta.EtlRunLog';
END
GO

/* -------------------------------------------------------------
   meta.EtlRunStep  -  รายละเอียดแต่ละขั้นภายในรอบเดียวกัน
   ------------------------------------------------------------- */
IF OBJECT_ID('meta.EtlRunStep') IS NULL
BEGIN
CREATE TABLE meta.EtlRunStep
(
    StepId          BIGINT          IDENTITY(1,1) NOT NULL,
    RunId           BIGINT          NOT NULL,
    StepNo          INT             NOT NULL,
    StepName        NVARCHAR(100)   NOT NULL,   -- Extract_Production
    SourceName      NVARCHAR(200)   NULL,       -- ชื่อไฟล์ / ชื่อตารางต้นทาง
    StartedAt       DATETIME2(0)    NOT NULL CONSTRAINT DF_Step_Start DEFAULT (SYSDATETIME()),
    FinishedAt      DATETIME2(0)    NULL,
    Status          VARCHAR(10)     NOT NULL CONSTRAINT DF_Step_Status DEFAULT ('RUNNING'),
    RowsRead        INT             NULL,
    RowsWritten     INT             NULL,
    RowsRejected    INT             NULL,
    Message         NVARCHAR(MAX)   NULL,

    CONSTRAINT PK_EtlRunStep PRIMARY KEY CLUSTERED (StepId),
    CONSTRAINT FK_EtlRunStep_Run FOREIGN KEY (RunId)
        REFERENCES meta.EtlRunLog(RunId) ON DELETE CASCADE
);
CREATE INDEX IX_EtlRunStep_Run ON meta.EtlRunStep(RunId, StepNo);
PRINT '>> Created meta.EtlRunStep';
END
GO

/* -------------------------------------------------------------
   meta.DataRejectLog  -  แถวที่ transform ไม่ผ่าน เก็บไว้ให้ตรวจสอบ
   ------------------------------------------------------------- */
IF OBJECT_ID('meta.DataRejectLog') IS NULL
BEGIN
CREATE TABLE meta.DataRejectLog
(
    RejectId        BIGINT          IDENTITY(1,1) NOT NULL,
    RunId           BIGINT          NULL,
    SourceTable     NVARCHAR(100)   NOT NULL,
    SourceRowId     BIGINT          NULL,
    RejectReason    NVARCHAR(300)   NOT NULL,   -- INVALID_DATE / UNKNOWN_DEPT / NEGATIVE_QTY
    RawPayload      NVARCHAR(MAX)   NULL,       -- ข้อมูลดิบของแถวนั้น (JSON)
    CreatedAt       DATETIME2(0)    NOT NULL CONSTRAINT DF_Reject_Created DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_DataRejectLog PRIMARY KEY CLUSTERED (RejectId)
);
CREATE INDEX IX_DataRejectLog_Run ON meta.DataRejectLog(RunId, SourceTable);
PRINT '>> Created meta.DataRejectLog';
END
GO

/* -------------------------------------------------------------
   meta.AuditLog  -  บันทึกการกระทำของผู้ใช้บนเว็บ
   ------------------------------------------------------------- */
IF OBJECT_ID('meta.AuditLog') IS NULL
BEGIN
CREATE TABLE meta.AuditLog
(
    AuditId         BIGINT          IDENTITY(1,1) NOT NULL,
    OccurredAt      DATETIME2(0)    NOT NULL CONSTRAINT DF_Audit_At DEFAULT (SYSDATETIME()),
    UserId          NVARCHAR(128)   NULL,       -- AspNetUsers.Id
    UserName        NVARCHAR(256)   NULL,
    ActionType      VARCHAR(30)     NOT NULL,   -- LOGIN / LOGIN_FAILED / EXPORT / KPI_EDIT
    EntityName      NVARCHAR(100)   NULL,
    EntityKey       NVARCHAR(100)   NULL,
    Detail          NVARCHAR(MAX)   NULL,
    IpAddress       NVARCHAR(45)    NULL,       -- รองรับ IPv6
    UserAgent       NVARCHAR(300)   NULL,
    IsSuccess       BIT             NOT NULL CONSTRAINT DF_Audit_Ok DEFAULT (1),

    CONSTRAINT PK_AuditLog PRIMARY KEY CLUSTERED (AuditId)
);
CREATE INDEX IX_AuditLog_User ON meta.AuditLog(UserName, OccurredAt DESC);
CREATE INDEX IX_AuditLog_Action ON meta.AuditLog(ActionType, OccurredAt DESC);
PRINT '>> Created meta.AuditLog';
END
GO

/* -------------------------------------------------------------
   meta.UserDepartment
   ผูก user ของ ASP.NET Identity กับแผนก -> ใช้ทำ row-level filter
   ใช้ nvarchar(128) ให้ตรงกับ AspNetUsers.Id
   ไม่ใส่ FK เพราะ Identity สร้างตารางทีหลังด้วย EF migration
   ------------------------------------------------------------- */
IF OBJECT_ID('meta.UserDepartment') IS NULL
BEGIN
CREATE TABLE meta.UserDepartment
(
    UserId          NVARCHAR(128)   NOT NULL,
    DepartmentId    INT             NOT NULL,
    IsPrimary       BIT             NOT NULL CONSTRAINT DF_UsrDept_Pri DEFAULT (1),
    CreatedAt       DATETIME2(0)    NOT NULL CONSTRAINT DF_UsrDept_Created DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_UserDepartment PRIMARY KEY CLUSTERED (UserId, DepartmentId)
);
PRINT '>> Created meta.UserDepartment';
END
GO

/* -------------------------------------------------------------
   meta.ReportDeliveryLog  -  บันทึกการส่งรายงานอัตโนมัติ
   ------------------------------------------------------------- */
IF OBJECT_ID('meta.ReportDeliveryLog') IS NULL
BEGIN
CREATE TABLE meta.ReportDeliveryLog
(
    DeliveryId      BIGINT          IDENTITY(1,1) NOT NULL,
    MonthKey        INT             NOT NULL,
    ReportName      NVARCHAR(100)   NOT NULL,
    FileFormat      VARCHAR(10)     NOT NULL,   -- XLSX / PDF
    FilePath        NVARCHAR(400)   NULL,
    FileSizeBytes   BIGINT          NULL,
    Recipients      NVARCHAR(MAX)   NULL,
    SentAt          DATETIME2(0)    NULL,
    Status          VARCHAR(10)     NOT NULL CONSTRAINT DF_Deliv_Status DEFAULT ('PENDING'),
    ErrorMessage    NVARCHAR(MAX)   NULL,
    RetryCount      INT             NOT NULL CONSTRAINT DF_Deliv_Retry DEFAULT (0),

    CONSTRAINT PK_ReportDeliveryLog PRIMARY KEY CLUSTERED (DeliveryId),
    CONSTRAINT CK_ReportDelivery_Status CHECK (Status IN ('PENDING','SENT','FAILED'))
);
CREATE INDEX IX_ReportDelivery_Month ON meta.ReportDeliveryLog(MonthKey, ReportName);
PRINT '>> Created meta.ReportDeliveryLog';
END
GO
