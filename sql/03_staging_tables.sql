/* =============================================================
   03_staging_tables.sql
   Purpose : ตาราง Staging รับข้อมูลดิบจาก 3 แหล่ง
   หลักการ : ทุก column เป็น NVARCHAR
             -> ข้อมูลสกปรก (วันที่ผิดรูป, ตัวเลขมี comma, ค่าว่าง)
                จะไม่ทำให้ load ล้มเหลว
             -> ค่อยไป validate/convert ตอน Transform เข้า core
   Idempotent : YES
   ============================================================= */

USE KpiMonthlyReport;
GO

/* -------------------------------------------------------------
   stg.ProductionRaw   <- แหล่งที่ 1 : SQL Server (จำลอง ERP)
   ------------------------------------------------------------- */
IF OBJECT_ID('stg.ProductionRaw') IS NULL
BEGIN
CREATE TABLE stg.ProductionRaw
(
    StgId           BIGINT          IDENTITY(1,1) NOT NULL,
    RunId           BIGINT          NULL,
    LoadedAt        DATETIME2(0)    NOT NULL CONSTRAINT DF_StgProd_At DEFAULT (SYSDATETIME()),
    SourceName      NVARCHAR(200)   NULL,

    ProdDate        NVARCHAR(50)    NULL,   -- อาจเป็น '2026-01-05' / '05/01/2026' / '5 Jan 26'
    DepartmentText  NVARCHAR(100)   NULL,   -- 'Line A' / 'line a' / 'LINE-A'
    ProductCode     NVARCHAR(50)    NULL,
    ShiftText       NVARCHAR(20)    NULL,
    QtyProducedText NVARCHAR(50)    NULL,   -- อาจมี comma / ค่าว่าง / ติดลบ
    QtyDefectText   NVARCHAR(50)    NULL,
    RunHoursText    NVARCHAR(50)    NULL,
    OperatorName    NVARCHAR(100)   NULL,

    IsProcessed     BIT             NOT NULL CONSTRAINT DF_StgProd_Proc DEFAULT (0),

    CONSTRAINT PK_ProductionRaw PRIMARY KEY CLUSTERED (StgId)
);
CREATE INDEX IX_ProductionRaw_Run ON stg.ProductionRaw(RunId, IsProcessed);
PRINT '>> Created stg.ProductionRaw';
END
GO

/* -------------------------------------------------------------
   stg.DowntimeRaw     <- แหล่งที่ 2 : ไฟล์ CSV รายวัน
   ------------------------------------------------------------- */
IF OBJECT_ID('stg.DowntimeRaw') IS NULL
BEGIN
CREATE TABLE stg.DowntimeRaw
(
    StgId           BIGINT          IDENTITY(1,1) NOT NULL,
    RunId           BIGINT          NULL,
    LoadedAt        DATETIME2(0)    NOT NULL CONSTRAINT DF_StgDown_At DEFAULT (SYSDATETIME()),
    SourceFileName  NVARCHAR(260)   NULL,
    SourceLineNo    INT             NULL,

    EventDate       NVARCHAR(50)    NULL,
    DepartmentText  NVARCHAR(100)   NULL,
    MachineCode     NVARCHAR(50)    NULL,
    ReasonCode      NVARCHAR(50)    NULL,
    ReasonText      NVARCHAR(200)   NULL,
    StartTimeText   NVARCHAR(50)    NULL,
    EndTimeText     NVARCHAR(50)    NULL,
    DurationMinText NVARCHAR(50)    NULL,

    IsProcessed     BIT             NOT NULL CONSTRAINT DF_StgDown_Proc DEFAULT (0),

    CONSTRAINT PK_DowntimeRaw PRIMARY KEY CLUSTERED (StgId)
);
CREATE INDEX IX_DowntimeRaw_Run ON stg.DowntimeRaw(RunId, IsProcessed);
PRINT '>> Created stg.DowntimeRaw';
END
GO

/* -------------------------------------------------------------
   stg.CostRaw         <- แหล่งที่ 3 : ไฟล์ Excel จากแผนกบัญชี
   ------------------------------------------------------------- */
IF OBJECT_ID('stg.CostRaw') IS NULL
BEGIN
CREATE TABLE stg.CostRaw
(
    StgId           BIGINT          IDENTITY(1,1) NOT NULL,
    RunId           BIGINT          NULL,
    LoadedAt        DATETIME2(0)    NOT NULL CONSTRAINT DF_StgCost_At DEFAULT (SYSDATETIME()),
    SourceFileName  NVARCHAR(260)   NULL,
    SourceSheetName NVARCHAR(100)   NULL,
    SourceRowNo     INT             NULL,

    PeriodText      NVARCHAR(50)    NULL,   -- 'Jan-26' / '2026-01' / '01/2026'
    DepartmentText  NVARCHAR(100)   NULL,
    CostTypeText    NVARCHAR(100)   NULL,   -- Material / Labor / Overhead / Utility
    AmountText      NVARCHAR(50)    NULL,   -- '1,234,567.89' / '(500)' ติดลบแบบบัญชี
    CurrencyText    NVARCHAR(10)    NULL,
    Remark          NVARCHAR(300)   NULL,

    IsProcessed     BIT             NOT NULL CONSTRAINT DF_StgCost_Proc DEFAULT (0),

    CONSTRAINT PK_CostRaw PRIMARY KEY CLUSTERED (StgId)
);
CREATE INDEX IX_CostRaw_Run ON stg.CostRaw(RunId, IsProcessed);
PRINT '>> Created stg.CostRaw';
END
GO

/* -------------------------------------------------------------
   stg.FileLoadHistory
   กันโหลดไฟล์เดิมซ้ำ - เช็คด้วย hash ของไฟล์ก่อน import
   ------------------------------------------------------------- */
IF OBJECT_ID('stg.FileLoadHistory') IS NULL
BEGIN
CREATE TABLE stg.FileLoadHistory
(
    FileLoadId      BIGINT          IDENTITY(1,1) NOT NULL,
    RunId           BIGINT          NULL,
    FileName        NVARCHAR(260)   NOT NULL,
    FileHash        CHAR(64)        NOT NULL,   -- SHA-256 hex
    FileSizeBytes   BIGINT          NULL,
    FileModifiedAt  DATETIME2(0)    NULL,
    RowCountLoaded  INT             NULL,
    LoadedAt        DATETIME2(0)    NOT NULL CONSTRAINT DF_FileHist_At DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_FileLoadHistory PRIMARY KEY CLUSTERED (FileLoadId),
    CONSTRAINT UQ_FileLoadHistory_Hash UNIQUE (FileHash)
);
PRINT '>> Created stg.FileLoadHistory';
END
GO
