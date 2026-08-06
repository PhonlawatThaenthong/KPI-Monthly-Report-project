/* =============================================================
   15_hr_tables.sql
   Purpose : ตารางสำหรับ KPI บุคลากรระดับแผนก
             DimEmployee + stg.AttendanceRaw + core.FactAttendance
   KPI ที่รองรับ : Attendance Rate, Overtime Hours, Absence Rate
   Idempotent : YES
   ============================================================= */

USE KpiMonthlyReport;
GO

/* -------------------------------------------------------------
   core.DimEmployee
   grain ของ KPI คือระดับแผนก (ทาง A) แต่ยังเก็บ dim รายคนไว้
   เพื่อให้ยกระดับเป็นรายบุคคล (ทาง B) ได้ในอนาคตโดยไม่ต้องรื้อ
   ------------------------------------------------------------- */
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
GO

-- แถว UNKNOWN สำหรับข้อมูลลงเวลาที่หาพนักงานไม่เจอ
IF NOT EXISTS (SELECT 1 FROM core.DimEmployee WHERE EmployeeId = -1)
BEGIN
    SET IDENTITY_INSERT core.DimEmployee ON;
    INSERT INTO core.DimEmployee (EmployeeId, EmployeeCode, EmployeeName, DepartmentId, IsActive)
    VALUES (-1, 'UNKNOWN', N'Unknown Employee', -1, 0);
    SET IDENTITY_INSERT core.DimEmployee OFF;
END
GO

/* -------------------------------------------------------------
   stg.AttendanceRaw  <- ระบบลงเวลา (จำลองเป็นไฟล์ CSV รายเดือน)
   ทุกคอลัมน์ NVARCHAR ตามหลักการเดิม
   ------------------------------------------------------------- */
IF OBJECT_ID('stg.AttendanceRaw') IS NULL
BEGIN
CREATE TABLE stg.AttendanceRaw
(
    StgId           BIGINT          IDENTITY(1,1) NOT NULL,
    RunId           BIGINT          NULL,
    LoadedAt        DATETIME2(0)    NOT NULL CONSTRAINT DF_StgAtt_At DEFAULT (SYSDATETIME()),
    SourceFileName  NVARCHAR(260)   NULL,
    SourceLineNo    INT             NULL,

    WorkDate        NVARCHAR(50)    NULL,
    EmployeeCode    NVARCHAR(50)    NULL,
    EmployeeName    NVARCHAR(150)   NULL,
    DepartmentText  NVARCHAR(100)   NULL,
    StatusText      NVARCHAR(50)    NULL,   -- PRESENT / ABSENT / LEAVE / SICK ...
    WorkHoursText   NVARCHAR(50)    NULL,
    OtHoursText     NVARCHAR(50)    NULL,

    IsProcessed     BIT             NOT NULL CONSTRAINT DF_StgAtt_Proc DEFAULT (0),

    CONSTRAINT PK_AttendanceRaw PRIMARY KEY CLUSTERED (StgId)
);
CREATE INDEX IX_AttendanceRaw_Run ON stg.AttendanceRaw(RunId, IsProcessed);
PRINT '>> Created stg.AttendanceRaw';
END
GO

/* -------------------------------------------------------------
   core.DimAttendanceStatus
   normalize สถานะการมางานให้เป็นมาตรฐาน
   ------------------------------------------------------------- */
IF OBJECT_ID('core.DimAttendanceStatus') IS NULL
BEGIN
CREATE TABLE core.DimAttendanceStatus
(
    StatusId        INT             IDENTITY(1,1) NOT NULL,
    StatusCode      VARCHAR(20)     NOT NULL,       -- PRESENT / ABSENT / LEAVE / SICK / HOLIDAY
    StatusName      NVARCHAR(50)    NOT NULL,
    -- นับเป็น "มางาน" ไหม (ใช้คำนวณ Attendance Rate)
    CountsAsPresent BIT             NOT NULL,
    -- นับเป็น "ขาด/ลา" ไหม (ใช้คำนวณ Absence Rate)
    CountsAsAbsence BIT             NOT NULL,
    -- นับเป็นวันทำงานที่ควรมาไหม (วันหยุดไม่นับเป็นตัวหาร)
    IsWorkingDay    BIT             NOT NULL,

    CONSTRAINT PK_DimAttendanceStatus PRIMARY KEY CLUSTERED (StatusId),
    CONSTRAINT UQ_DimAttendanceStatus_Code UNIQUE (StatusCode)
);
PRINT '>> Created core.DimAttendanceStatus';
END
GO

-- แถว UNKNOWN
IF NOT EXISTS (SELECT 1 FROM core.DimAttendanceStatus WHERE StatusId = -1)
BEGIN
    SET IDENTITY_INSERT core.DimAttendanceStatus ON;
    INSERT INTO core.DimAttendanceStatus
        (StatusId, StatusCode, StatusName, CountsAsPresent, CountsAsAbsence, IsWorkingDay)
    VALUES (-1, 'UNKNOWN', N'Unknown Status', 0, 0, 0);
    SET IDENTITY_INSERT core.DimAttendanceStatus OFF;
END
GO

/* -------------------------------------------------------------
   core.FactAttendance
   grain : 1 พนักงาน x 1 วัน
   แม้ KPI จะวัดระดับแผนก แต่เก็บ fact รายคนรายวันไว้
   เพื่อความยืดหยุ่นและตรวจสอบย้อนหลังได้
   ------------------------------------------------------------- */
IF OBJECT_ID('core.FactAttendance') IS NULL
BEGIN
CREATE TABLE core.FactAttendance
(
    AttendanceKey   BIGINT          IDENTITY(1,1) NOT NULL,
    DateKey         INT             NOT NULL,
    MonthKey        INT             NOT NULL,
    EmployeeId      INT             NOT NULL,
    DepartmentId    INT             NOT NULL,
    StatusId        INT             NOT NULL,

    WorkHours       DECIMAL(8,2)    NOT NULL CONSTRAINT DF_FactAtt_Work DEFAULT (0),
    OtHours         DECIMAL(8,2)    NOT NULL CONSTRAINT DF_FactAtt_Ot DEFAULT (0),

    SourceRunId     BIGINT          NULL,
    LoadedAt        DATETIME2(0)    NOT NULL CONSTRAINT DF_FactAtt_At DEFAULT (SYSDATETIME()),

    CONSTRAINT PK_FactAttendance PRIMARY KEY CLUSTERED (AttendanceKey),
    -- 1 คน 1 วัน มีได้แถวเดียว
    CONSTRAINT UQ_FactAttendance_NK UNIQUE (DateKey, EmployeeId),
    CONSTRAINT FK_FactAttendance_Date FOREIGN KEY (DateKey) REFERENCES core.DimDate(DateKey),
    CONSTRAINT FK_FactAttendance_Emp FOREIGN KEY (EmployeeId) REFERENCES core.DimEmployee(EmployeeId),
    CONSTRAINT FK_FactAttendance_Dept FOREIGN KEY (DepartmentId) REFERENCES core.DimDepartment(DepartmentId),
    CONSTRAINT FK_FactAttendance_Status FOREIGN KEY (StatusId) REFERENCES core.DimAttendanceStatus(StatusId),
    CONSTRAINT CK_FactAttendance_Hours CHECK (WorkHours >= 0 AND OtHours >= 0)
);
CREATE INDEX IX_FactAttendance_Month ON core.FactAttendance(MonthKey, DepartmentId)
    INCLUDE (StatusId, OtHours);
PRINT '>> Created core.FactAttendance';
END
GO

/* -------------------------------------------------------------
   core.EmployeeAlias  -  จับคู่รหัส/ชื่อพนักงานที่สะกดไม่ตรง
   (เผื่อระบบลงเวลาส่งรหัสมาหลายรูปแบบ)
   ------------------------------------------------------------- */
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
GO
