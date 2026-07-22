/* =============================================================
   04_core_tables.sql
   Purpose : Dimension + Fact ชั้น core (ข้อมูลที่ clean แล้ว)
   Idempotent : YES
   ============================================================= */

USE KpiMonthlyReport;
GO

/* =============================================================
   DIMENSIONS
   ============================================================= */

/* -------------------------------------------------------------
   core.DimDate  -  DateKey = yyyymmdd, MonthKey = yyyymm
   ------------------------------------------------------------- */
IF OBJECT_ID('core.DimDate') IS NULL
BEGIN
CREATE TABLE core.DimDate
(
    DateKey         INT             NOT NULL,       -- 20260115
    FullDate        DATE            NOT NULL,
    MonthKey        INT             NOT NULL,       -- 202601
    [Year]          SMALLINT        NOT NULL,
    [Quarter]       TINYINT         NOT NULL,
    MonthNo         TINYINT         NOT NULL,
    MonthNameEn     VARCHAR(20)     NOT NULL,
    MonthNameTh     NVARCHAR(20)    NOT NULL,
    MonthShortEn    CHAR(3)         NOT NULL,
    DayOfMonth      TINYINT         NOT NULL,
    DayOfWeekNo     TINYINT         NOT NULL,       -- 1=Mon .. 7=Sun
    DayNameEn       VARCHAR(15)     NOT NULL,
    WeekOfYear      TINYINT         NOT NULL,
    IsWeekend       BIT             NOT NULL,
    FiscalYear      SMALLINT        NOT NULL,       -- ปีงบ เริ่ม ต.ค. (แก้ได้ใน seed)
    FiscalQuarter   TINYINT         NOT NULL,

    CONSTRAINT PK_DimDate PRIMARY KEY CLUSTERED (DateKey)
);
CREATE INDEX IX_DimDate_Month ON core.DimDate(MonthKey) INCLUDE (FullDate);
CREATE UNIQUE INDEX UX_DimDate_FullDate ON core.DimDate(FullDate);
PRINT '>> Created core.DimDate';
END
GO

/* -------------------------------------------------------------
   core.DimDepartment
   ------------------------------------------------------------- */
IF OBJECT_ID('core.DimDepartment') IS NULL
BEGIN
CREATE TABLE core.DimDepartment
(
    DepartmentId    INT             IDENTITY(1,1) NOT NULL,
    DepartmentCode  VARCHAR(20)     NOT NULL,       -- LINE_A
    DepartmentName  NVARCHAR(100)   NOT NULL,       -- Line A
    DepartmentNameTh NVARCHAR(100)  NULL,
    PlantCode       VARCHAR(20)     NULL,
    ManagerName     NVARCHAR(100)   NULL,
    IsActive        BIT             NOT NULL CONSTRAINT DF_Dept_Active DEFAULT (1),
    CreatedAt       DATETIME2(0)    NOT NULL CONSTRAINT DF_Dept_Created DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_DimDepartment PRIMARY KEY CLUSTERED (DepartmentId),
    CONSTRAINT UQ_DimDepartment_Code UNIQUE (DepartmentCode)
);
PRINT '>> Created core.DimDepartment';
END
GO

-- แถวพิเศษสำหรับข้อมูลที่หาแผนกไม่เจอ (Unknown member) -> ไม่ทิ้งข้อมูล
IF NOT EXISTS (SELECT 1 FROM core.DimDepartment WHERE DepartmentCode = 'UNKNOWN')
BEGIN
    SET IDENTITY_INSERT core.DimDepartment ON;
    INSERT INTO core.DimDepartment (DepartmentId, DepartmentCode, DepartmentName, DepartmentNameTh, IsActive)
    VALUES (-1, 'UNKNOWN', 'Unknown Department', N'ไม่ระบุแผนก', 0);
    SET IDENTITY_INSERT core.DimDepartment OFF;
END
GO

/* -------------------------------------------------------------
   core.DepartmentAlias
   ตารางแปลงชื่อสกปรกจากต้นทาง -> DepartmentId
   'line a' / 'LINE-A' / 'Line  A' ทั้งหมด map เป็นตัวเดียวกัน
   ------------------------------------------------------------- */
IF OBJECT_ID('core.DepartmentAlias') IS NULL
BEGIN
CREATE TABLE core.DepartmentAlias
(
    AliasId         INT             IDENTITY(1,1) NOT NULL,
    AliasText       NVARCHAR(100)   NOT NULL,       -- เก็บแบบ normalize แล้ว (lower, ตัดช่องว่าง/ขีด)
    DepartmentId    INT             NOT NULL,
    CreatedAt       DATETIME2(0)    NOT NULL CONSTRAINT DF_Alias_Created DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_DepartmentAlias PRIMARY KEY CLUSTERED (AliasId),
    CONSTRAINT UQ_DepartmentAlias_Text UNIQUE (AliasText),
    CONSTRAINT FK_DepartmentAlias_Dept FOREIGN KEY (DepartmentId)
        REFERENCES core.DimDepartment(DepartmentId)
);
PRINT '>> Created core.DepartmentAlias';
END
GO

/* -------------------------------------------------------------
   core.DimProduct
   ------------------------------------------------------------- */
IF OBJECT_ID('core.DimProduct') IS NULL
BEGIN
CREATE TABLE core.DimProduct
(
    ProductId       INT             IDENTITY(1,1) NOT NULL,
    ProductCode     VARCHAR(50)     NOT NULL,
    ProductName     NVARCHAR(150)   NOT NULL,
    ProductGroup    NVARCHAR(50)    NULL,
    StandardCost    DECIMAL(18,4)   NULL,
    UnitOfMeasure   NVARCHAR(20)    NULL,
    IsActive        BIT             NOT NULL CONSTRAINT DF_Prod_Active DEFAULT (1),

    CONSTRAINT PK_DimProduct PRIMARY KEY CLUSTERED (ProductId),
    CONSTRAINT UQ_DimProduct_Code UNIQUE (ProductCode)
);
PRINT '>> Created core.DimProduct';
END
GO

IF NOT EXISTS (SELECT 1 FROM core.DimProduct WHERE ProductCode = 'UNKNOWN')
BEGIN
    SET IDENTITY_INSERT core.DimProduct ON;
    INSERT INTO core.DimProduct (ProductId, ProductCode, ProductName, IsActive)
    VALUES (-1, 'UNKNOWN', 'Unknown Product', 0);
    SET IDENTITY_INSERT core.DimProduct OFF;
END
GO

/* -------------------------------------------------------------
   core.DimCostType
   ------------------------------------------------------------- */
IF OBJECT_ID('core.DimCostType') IS NULL
BEGIN
CREATE TABLE core.DimCostType
(
    CostTypeId      INT             IDENTITY(1,1) NOT NULL,
    CostTypeCode    VARCHAR(20)     NOT NULL,       -- MATERIAL / LABOR / OVERHEAD / UTILITY
    CostTypeName    NVARCHAR(100)   NOT NULL,
    IsVariable      BIT             NOT NULL CONSTRAINT DF_CostType_Var DEFAULT (1),

    CONSTRAINT PK_DimCostType PRIMARY KEY CLUSTERED (CostTypeId),
    CONSTRAINT UQ_DimCostType_Code UNIQUE (CostTypeCode)
);
PRINT '>> Created core.DimCostType';
END
GO

IF NOT EXISTS (SELECT 1 FROM core.DimCostType WHERE CostTypeCode = 'UNKNOWN')
BEGIN
    SET IDENTITY_INSERT core.DimCostType ON;
    INSERT INTO core.DimCostType (CostTypeId, CostTypeCode, CostTypeName, IsVariable)
    VALUES (-1, 'UNKNOWN', 'Unknown Cost Type', 0);
    SET IDENTITY_INSERT core.DimCostType OFF;
END
GO


/* =============================================================
   FACTS
   ทุก Fact มี UNIQUE บน natural key -> รองรับ MERGE = Idempotent
   ============================================================= */

/* -------------------------------------------------------------
   core.FactProduction  (grain: วัน x แผนก x สินค้า x กะ)
   ------------------------------------------------------------- */
IF OBJECT_ID('core.FactProduction') IS NULL
BEGIN
CREATE TABLE core.FactProduction
(
    ProductionKey   BIGINT          IDENTITY(1,1) NOT NULL,
    DateKey         INT             NOT NULL,
    MonthKey        INT             NOT NULL,
    DepartmentId    INT             NOT NULL,
    ProductId       INT             NOT NULL,
    ShiftNo         TINYINT         NOT NULL CONSTRAINT DF_FactProd_Shift DEFAULT (1),

    QtyProduced     DECIMAL(18,2)   NOT NULL CONSTRAINT DF_FactProd_Qty DEFAULT (0),
    QtyDefect       DECIMAL(18,2)   NOT NULL CONSTRAINT DF_FactProd_Def DEFAULT (0),
    QtyGood         AS (QtyProduced - QtyDefect) PERSISTED,
    RunHours        DECIMAL(10,2)   NOT NULL CONSTRAINT DF_FactProd_Hrs DEFAULT (0),

    SourceRunId     BIGINT          NULL,
    LoadedAt        DATETIME2(0)    NOT NULL CONSTRAINT DF_FactProd_At DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_FactProduction PRIMARY KEY CLUSTERED (ProductionKey),
    CONSTRAINT UQ_FactProduction_NK UNIQUE (DateKey, DepartmentId, ProductId, ShiftNo),
    CONSTRAINT FK_FactProduction_Date FOREIGN KEY (DateKey) REFERENCES core.DimDate(DateKey),
    CONSTRAINT FK_FactProduction_Dept FOREIGN KEY (DepartmentId) REFERENCES core.DimDepartment(DepartmentId),
    CONSTRAINT FK_FactProduction_Prod FOREIGN KEY (ProductId) REFERENCES core.DimProduct(ProductId),
    CONSTRAINT CK_FactProduction_Qty CHECK (QtyProduced >= 0 AND QtyDefect >= 0 AND QtyDefect <= QtyProduced)
);
CREATE INDEX IX_FactProduction_Month ON core.FactProduction(MonthKey, DepartmentId)
    INCLUDE (QtyProduced, QtyDefect, RunHours);
PRINT '>> Created core.FactProduction';
END
GO

/* -------------------------------------------------------------
   core.FactDowntime  (grain: 1 เหตุการณ์)
   ------------------------------------------------------------- */
IF OBJECT_ID('core.FactDowntime') IS NULL
BEGIN
CREATE TABLE core.FactDowntime
(
    DowntimeKey     BIGINT          IDENTITY(1,1) NOT NULL,
    DateKey         INT             NOT NULL,
    MonthKey        INT             NOT NULL,
    DepartmentId    INT             NOT NULL,
    MachineCode     VARCHAR(50)     NOT NULL CONSTRAINT DF_FactDown_Mc DEFAULT ('UNKNOWN'),
    ReasonCode      VARCHAR(50)     NOT NULL CONSTRAINT DF_FactDown_Rc DEFAULT ('UNKNOWN'),

    StartTime       DATETIME2(0)    NULL,
    EndTime         DATETIME2(0)    NULL,
    DurationMinutes DECIMAL(10,2)   NOT NULL CONSTRAINT DF_FactDown_Dur DEFAULT (0),
    DurationHours   AS (DurationMinutes / 60.0) PERSISTED,

    SourceRunId     BIGINT          NULL,
    SourceFileName  NVARCHAR(260)   NULL,
    LoadedAt        DATETIME2(0)    NOT NULL CONSTRAINT DF_FactDown_At DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_FactDowntime PRIMARY KEY CLUSTERED (DowntimeKey),
    CONSTRAINT UQ_FactDowntime_NK UNIQUE (DateKey, DepartmentId, MachineCode, ReasonCode, StartTime),
    CONSTRAINT FK_FactDowntime_Date FOREIGN KEY (DateKey) REFERENCES core.DimDate(DateKey),
    CONSTRAINT FK_FactDowntime_Dept FOREIGN KEY (DepartmentId) REFERENCES core.DimDepartment(DepartmentId),
    CONSTRAINT CK_FactDowntime_Dur CHECK (DurationMinutes >= 0)
);
CREATE INDEX IX_FactDowntime_Month ON core.FactDowntime(MonthKey, DepartmentId)
    INCLUDE (DurationMinutes);
PRINT '>> Created core.FactDowntime';
END
GO

/* -------------------------------------------------------------
   core.FactCost  (grain: เดือน x แผนก x ประเภทต้นทุน)
   ------------------------------------------------------------- */
IF OBJECT_ID('core.FactCost') IS NULL
BEGIN
CREATE TABLE core.FactCost
(
    CostKey         BIGINT          IDENTITY(1,1) NOT NULL,
    MonthKey        INT             NOT NULL,
    DepartmentId    INT             NOT NULL,
    CostTypeId      INT             NOT NULL,

    Amount          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_FactCost_Amt DEFAULT (0),
    CurrencyCode    CHAR(3)         NOT NULL CONSTRAINT DF_FactCost_Cur DEFAULT ('THB'),

    SourceRunId     BIGINT          NULL,
    SourceFileName  NVARCHAR(260)   NULL,
    LoadedAt        DATETIME2(0)    NOT NULL CONSTRAINT DF_FactCost_At DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_FactCost PRIMARY KEY CLUSTERED (CostKey),
    CONSTRAINT UQ_FactCost_NK UNIQUE (MonthKey, DepartmentId, CostTypeId),
    CONSTRAINT FK_FactCost_Dept FOREIGN KEY (DepartmentId) REFERENCES core.DimDepartment(DepartmentId),
    CONSTRAINT FK_FactCost_Type FOREIGN KEY (CostTypeId) REFERENCES core.DimCostType(CostTypeId)
);
CREATE INDEX IX_FactCost_Month ON core.FactCost(MonthKey, DepartmentId) INCLUDE (Amount);
PRINT '>> Created core.FactCost';
END
GO

/* -------------------------------------------------------------
   core.FactKpiMonthly
   ผลลัพธ์ KPI ที่คำนวณเสร็จแล้ว - Dashboard/Report อ่านจากตรงนี้
   DepartmentId = -99 หมายถึงระดับรวมทั้งบริษัท
   ------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM core.DimDepartment WHERE DepartmentId = -99)
BEGIN
    SET IDENTITY_INSERT core.DimDepartment ON;
    INSERT INTO core.DimDepartment (DepartmentId, DepartmentCode, DepartmentName, DepartmentNameTh, IsActive)
    VALUES (-99, 'ALL', 'All Departments', N'รวมทุกแผนก', 1);
    SET IDENTITY_INSERT core.DimDepartment OFF;
END
GO

IF OBJECT_ID('core.FactKpiMonthly') IS NULL
BEGIN
CREATE TABLE core.FactKpiMonthly
(
    KpiFactKey      BIGINT          IDENTITY(1,1) NOT NULL,
    MonthKey        INT             NOT NULL,
    KpiId           INT             NOT NULL,
    DepartmentId    INT             NOT NULL,

    ActualValue     DECIMAL(18,4)   NULL,
    TargetValue     DECIMAL(18,4)   NULL,
    BaselineValue   DECIMAL(18,4)   NULL,
    PrevMonthValue  DECIMAL(18,4)   NULL,

    Variance        AS (ActualValue - TargetValue) PERSISTED,

    -- ระวังหารศูนย์ด้วย NULLIF เสมอ
    AchievementPct  AS (CASE WHEN TargetValue IS NULL OR TargetValue = 0 THEN NULL
                             ELSE (ActualValue / NULLIF(TargetValue,0)) * 100 END),
    MoMChangePct    AS (CASE WHEN PrevMonthValue IS NULL OR PrevMonthValue = 0 THEN NULL
                             ELSE ((ActualValue - PrevMonthValue) / NULLIF(PrevMonthValue,0)) * 100 END),

    StatusFlag      VARCHAR(10)     NULL,       -- GREEN / YELLOW / RED
    Numerator       DECIMAL(18,4)   NULL,       -- เก็บตัวตั้ง/ตัวหารไว้ตรวจย้อนหลัง
    Denominator     DECIMAL(18,4)   NULL,

    CalculatedAt    DATETIME2(0)    NOT NULL CONSTRAINT DF_FactKpi_At DEFAULT (SYSDATETIME()),
    SourceRunId     BIGINT          NULL,

    CONSTRAINT PK_FactKpiMonthly PRIMARY KEY CLUSTERED (KpiFactKey),
    CONSTRAINT UQ_FactKpiMonthly_NK UNIQUE (MonthKey, KpiId, DepartmentId),
    CONSTRAINT FK_FactKpiMonthly_Kpi FOREIGN KEY (KpiId) REFERENCES meta.KpiDefinition(KpiId),
    CONSTRAINT FK_FactKpiMonthly_Dept FOREIGN KEY (DepartmentId) REFERENCES core.DimDepartment(DepartmentId),
    CONSTRAINT CK_FactKpiMonthly_Status CHECK (StatusFlag IN ('GREEN','YELLOW','RED') OR StatusFlag IS NULL)
);
CREATE INDEX IX_FactKpiMonthly_Month ON core.FactKpiMonthly(MonthKey, DepartmentId)
    INCLUDE (KpiId, ActualValue, TargetValue);
CREATE INDEX IX_FactKpiMonthly_Kpi ON core.FactKpiMonthly(KpiId, MonthKey);
PRINT '>> Created core.FactKpiMonthly';
END
GO

/* -- FK ย้อนกลับของ meta.KpiTarget -> DimDepartment (สร้างทีหลังเพราะลำดับตาราง) -- */
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_KpiTarget_Dept')
    ALTER TABLE meta.KpiTarget ADD CONSTRAINT FK_KpiTarget_Dept
        FOREIGN KEY (DepartmentId) REFERENCES core.DimDepartment(DepartmentId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_UserDepartment_Dept')
    ALTER TABLE meta.UserDepartment ADD CONSTRAINT FK_UserDepartment_Dept
        FOREIGN KEY (DepartmentId) REFERENCES core.DimDepartment(DepartmentId);
GO
