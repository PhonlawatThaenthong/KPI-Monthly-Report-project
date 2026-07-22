/* =============================================================
   05_utility_procs.sql
   Purpose : Stored Procedure ที่ ETL / Web เรียกใช้ประจำ
   Idempotent : YES (ใช้ CREATE OR ALTER - ต้องการ SQL Server 2016 SP1+)
   ============================================================= */

USE KpiMonthlyReport;
GO

/* -------------------------------------------------------------
   meta.usp_EtlRun_Start
   ------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE meta.usp_EtlRun_Start
    @JobName        NVARCHAR(100),
    @MonthKey       INT             = NULL,
    @TriggeredBy    NVARCHAR(128)   = NULL,
    @MachineName    NVARCHAR(100)   = NULL,
    @RunId          BIGINT          OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO meta.EtlRunLog (JobName, MonthKey, TriggeredBy, MachineName, Status)
    VALUES (@JobName, @MonthKey, ISNULL(@TriggeredBy, SUSER_SNAME()),
            ISNULL(@MachineName, HOST_NAME()), 'RUNNING');

    SET @RunId = SCOPE_IDENTITY();
END
GO

/* -------------------------------------------------------------
   meta.usp_EtlRun_Finish
   ------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE meta.usp_EtlRun_Finish
    @RunId          BIGINT,
    @Status         VARCHAR(10),        -- SUCCESS / FAILED / WARNING
    @RowsRead       INT             = NULL,
    @RowsWritten    INT             = NULL,
    @RowsRejected   INT             = NULL,
    @ErrorMessage   NVARCHAR(MAX)   = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE meta.EtlRunLog
    SET FinishedAt   = SYSDATETIME(),
        Status       = @Status,
        RowsRead     = ISNULL(@RowsRead, RowsRead),
        RowsWritten  = ISNULL(@RowsWritten, RowsWritten),
        RowsRejected = ISNULL(@RowsRejected, RowsRejected),
        ErrorMessage = @ErrorMessage
    WHERE RunId = @RunId;
END
GO

/* -------------------------------------------------------------
   meta.usp_EtlStep_Log   (log แบบขั้นตอนย่อย)
   ------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE meta.usp_EtlStep_Log
    @RunId          BIGINT,
    @StepNo         INT,
    @StepName       NVARCHAR(100),
    @SourceName     NVARCHAR(200)   = NULL,
    @Status         VARCHAR(10)     = 'SUCCESS',
    @RowsRead       INT             = NULL,
    @RowsWritten    INT             = NULL,
    @RowsRejected   INT             = NULL,
    @Message        NVARCHAR(MAX)   = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO meta.EtlRunStep
        (RunId, StepNo, StepName, SourceName, FinishedAt,
         Status, RowsRead, RowsWritten, RowsRejected, Message)
    VALUES
        (@RunId, @StepNo, @StepName, @SourceName, SYSDATETIME(),
         @Status, @RowsRead, @RowsWritten, @RowsRejected, @Message);
END
GO

/* -------------------------------------------------------------
   meta.usp_Audit_Write   (Web เรียกทุกครั้งที่ user ทำอะไรสำคัญ)
   ------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE meta.usp_Audit_Write
    @UserId         NVARCHAR(128)   = NULL,
    @UserName       NVARCHAR(256)   = NULL,
    @ActionType     VARCHAR(30),
    @EntityName     NVARCHAR(100)   = NULL,
    @EntityKey      NVARCHAR(100)   = NULL,
    @Detail         NVARCHAR(MAX)   = NULL,
    @IpAddress      NVARCHAR(45)    = NULL,
    @UserAgent      NVARCHAR(300)   = NULL,
    @IsSuccess      BIT             = 1
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO meta.AuditLog
        (UserId, UserName, ActionType, EntityName, EntityKey,
         Detail, IpAddress, UserAgent, IsSuccess)
    VALUES
        (@UserId, @UserName, @ActionType, @EntityName, @EntityKey,
         @Detail, @IpAddress, @UserAgent, @IsSuccess);
END
GO

/* -------------------------------------------------------------
   core.fn_NormalizeText
   ใช้ทำ key สำหรับจับคู่ชื่อแผนกที่สะกดไม่ตรงกัน
   'LINE-A ' -> 'linea'
   ------------------------------------------------------------- */
CREATE OR ALTER FUNCTION core.fn_NormalizeText (@Input NVARCHAR(200))
RETURNS NVARCHAR(200)
WITH SCHEMABINDING
AS
BEGIN
    DECLARE @s NVARCHAR(200) = LOWER(LTRIM(RTRIM(ISNULL(@Input, N''))));
    SET @s = REPLACE(@s, N' ',  N'');
    SET @s = REPLACE(@s, N'-',  N'');
    SET @s = REPLACE(@s, N'_',  N'');
    SET @s = REPLACE(@s, N'.',  N'');
    SET @s = REPLACE(@s, NCHAR(9),  N'');   -- tab
    RETURN @s;
END
GO

/* -------------------------------------------------------------
   core.fn_ParseDecimal
   แปลงข้อความตัวเลขแบบสกปรกเป็น DECIMAL
   '1,234.50' -> 1234.50   |   '(500)' -> -500   |   'N/A' -> NULL
   ------------------------------------------------------------- */
CREATE OR ALTER FUNCTION core.fn_ParseDecimal (@Input NVARCHAR(100))
RETURNS DECIMAL(18,4)
AS
BEGIN
    DECLARE @s NVARCHAR(100) = LTRIM(RTRIM(ISNULL(@Input, N'')));
    DECLARE @neg BIT = 0;

    IF @s = N'' RETURN NULL;

    -- รูปแบบบัญชี (500) = ติดลบ
    IF LEFT(@s,1) = N'(' AND RIGHT(@s,1) = N')'
    BEGIN
        SET @neg = 1;
        SET @s = SUBSTRING(@s, 2, LEN(@s) - 2);
    END

    SET @s = REPLACE(@s, N',', N'');
    SET @s = REPLACE(@s, N' ', N'');
    SET @s = REPLACE(@s, N'฿', N'');

    IF TRY_CONVERT(DECIMAL(18,4), @s) IS NULL RETURN NULL;

    RETURN CASE WHEN @neg = 1
                THEN -ABS(TRY_CONVERT(DECIMAL(18,4), @s))
                ELSE TRY_CONVERT(DECIMAL(18,4), @s) END;
END
GO

/* -------------------------------------------------------------
   core.usp_ResolveDepartment
   หา DepartmentId จากข้อความต้นทาง ถ้าหาไม่เจอคืน -1 (Unknown)
   ------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE core.usp_ResolveDepartment
    @DepartmentText NVARCHAR(100),
    @DepartmentId   INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @norm NVARCHAR(200) = core.fn_NormalizeText(@DepartmentText);

    SELECT TOP 1 @DepartmentId = a.DepartmentId
    FROM core.DepartmentAlias a
    WHERE a.AliasText = @norm;

    IF @DepartmentId IS NULL
        SELECT TOP 1 @DepartmentId = d.DepartmentId
        FROM core.DimDepartment d
        WHERE core.fn_NormalizeText(d.DepartmentCode) = @norm
           OR core.fn_NormalizeText(d.DepartmentName) = @norm;

    SET @DepartmentId = ISNULL(@DepartmentId, -1);
END
GO

/* -------------------------------------------------------------
   core.usp_DimDate_Populate
   สร้างข้อมูลปฏิทิน (idempotent - insert เฉพาะวันที่ยังไม่มี)
   FiscalYear เริ่มเดือน ต.ค. แก้ @FiscalStartMonth ได้ตามบริษัท
   ------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE core.usp_DimDate_Populate
    @StartDate DATE,
    @EndDate   DATE,
    @FiscalStartMonth TINYINT = 10
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH n AS (
        SELECT TOP (DATEDIFF(DAY, @StartDate, @EndDate) + 1)
               ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i
        FROM sys.all_objects a CROSS JOIN sys.all_objects b
    ),
    d AS (
        SELECT DATEADD(DAY, i, @StartDate) AS FullDate FROM n
    )
    INSERT INTO core.DimDate
        (DateKey, FullDate, MonthKey, [Year], [Quarter], MonthNo,
         MonthNameEn, MonthNameTh, MonthShortEn, DayOfMonth, DayOfWeekNo,
         DayNameEn, WeekOfYear, IsWeekend, FiscalYear, FiscalQuarter)
    SELECT
        CONVERT(INT, FORMAT(d.FullDate, 'yyyyMMdd')),
        d.FullDate,
        CONVERT(INT, FORMAT(d.FullDate, 'yyyyMM')),
        YEAR(d.FullDate),
        DATEPART(QUARTER, d.FullDate),
        MONTH(d.FullDate),
        DATENAME(MONTH, d.FullDate),
        CHOOSE(MONTH(d.FullDate), N'มกราคม', N'กุมภาพันธ์', N'มีนาคม', N'เมษายน',
               N'พฤษภาคม', N'มิถุนายน', N'กรกฎาคม', N'สิงหาคม',
               N'กันยายน', N'ตุลาคม', N'พฤศจิกายน', N'ธันวาคม'),
        LEFT(DATENAME(MONTH, d.FullDate), 3),
        DAY(d.FullDate),
        ((DATEPART(WEEKDAY, d.FullDate) + @@DATEFIRST - 2) % 7) + 1,
        DATENAME(WEEKDAY, d.FullDate),
        DATEPART(ISO_WEEK, d.FullDate),
        CASE WHEN ((DATEPART(WEEKDAY, d.FullDate) + @@DATEFIRST - 2) % 7) + 1 >= 6 THEN 1 ELSE 0 END,
        CASE WHEN MONTH(d.FullDate) >= @FiscalStartMonth
             THEN YEAR(d.FullDate) + 1 ELSE YEAR(d.FullDate) END,
        ((( MONTH(d.FullDate) - @FiscalStartMonth + 12) % 12) / 3) + 1
    FROM d
    WHERE NOT EXISTS (
        SELECT 1 FROM core.DimDate x
        WHERE x.DateKey = CONVERT(INT, FORMAT(d.FullDate, 'yyyyMMdd'))
    );

    PRINT CONCAT('>> DimDate rows inserted: ', @@ROWCOUNT);
END
GO

/* -------------------------------------------------------------
   core.usp_PurgeMonth
   ลบข้อมูลของเดือนที่ระบุก่อนโหลดใหม่ = ทำให้ ETL Idempotent
   (ทางเลือกแทนการใช้ MERGE ทุก fact)
   ------------------------------------------------------------- */
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
            DELETE FROM core.FactKpiMonthly WHERE MonthKey = @MonthKey;
            DELETE FROM core.FactCost       WHERE MonthKey = @MonthKey;
            DELETE FROM core.FactDowntime   WHERE MonthKey = @MonthKey;
            DELETE FROM core.FactProduction WHERE MonthKey = @MonthKey;
        COMMIT TRAN;
        PRINT CONCAT('>> Purged month ', @MonthKey);
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO
