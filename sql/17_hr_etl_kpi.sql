/* =============================================================
   17_hr_etl_kpi.sql
   Purpose : Transform ข้อมูลลงเวลา + KPI proc บุคลากร 3 ตัว
   Idempotent : YES
   ============================================================= */

USE KpiMonthlyReport;
GO

/* =============================================================
   TRANSFORM : stg.AttendanceRaw -> core.FactAttendance
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_Transform_Attendance
    @RunId          BIGINT,
    @RowsWritten    INT OUTPUT,
    @RowsRejected   INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @RowsWritten = 0;
    SET @RowsRejected = 0;

    BEGIN TRY
        BEGIN TRAN;

        /* 1) PARSE + resolve dimension */
        IF OBJECT_ID('tempdb..#parsed') IS NOT NULL DROP TABLE #parsed;

        SELECT
            r.StgId,
            core.fn_ParseDate(r.WorkDate)                          AS WorkDate,
            ISNULL(e.EmployeeId, -1)                               AS EmployeeId,
            -- แผนก: เอาของพนักงานเป็นหลัก ถ้าหาคนไม่เจอค่อยดูจากข้อความ
            COALESCE(e.DepartmentId, a.DepartmentId, -1)           AS DepartmentId,
            ISNULL(st.StatusId, -1)                                AS StatusId,
            ISNULL(core.fn_ParseDecimal(r.WorkHoursText), 0)       AS WorkHours,
            ISNULL(core.fn_ParseDecimal(r.OtHoursText), 0)         AS OtHours,
            r.WorkDate      AS RawDate,
            r.EmployeeCode  AS RawEmp,
            r.StatusText    AS RawStatus,
            r.OtHoursText   AS RawOt
        INTO #parsed
        FROM stg.AttendanceRaw r
        LEFT JOIN core.DimEmployee e
               ON e.EmployeeCode = LTRIM(RTRIM(r.EmployeeCode))
        LEFT JOIN core.DepartmentAlias a
               ON a.AliasText = core.fn_NormalizeText(r.DepartmentText)
        LEFT JOIN core.DimAttendanceStatus st
               ON core.fn_NormalizeText(st.StatusCode) = core.fn_NormalizeText(r.StatusText)
        WHERE r.RunId = @RunId;

        /* 2) จำแนก reject */
        IF OBJECT_ID('tempdb..#classified') IS NOT NULL DROP TABLE #classified;

        SELECT p.*, d.DateKey, d.MonthKey,
            CASE
                WHEN p.WorkDate IS NULL          THEN 'INVALID_DATE'
                WHEN d.DateKey IS NULL           THEN 'DATE_OUT_OF_CALENDAR'
                WHEN p.OtHours < 0               THEN 'NEGATIVE_OT'
                -- สถานะไม่รู้จัก ไม่ reject แต่ไป UNKNOWN (คำนวณ KPI จะข้ามให้เอง)
                ELSE NULL
            END AS RejectReason
        INTO #classified
        FROM #parsed p
        LEFT JOIN core.DimDate d ON d.FullDate = p.WorkDate;

        INSERT INTO meta.DataRejectLog (RunId, SourceTable, SourceRowId, RejectReason, RawPayload)
        SELECT @RunId, 'stg.AttendanceRaw', c.StgId, c.RejectReason,
            CONCAT('{"WorkDate":"', STRING_ESCAPE(ISNULL(c.RawDate,''),'json'),
                   '","Emp":"',     STRING_ESCAPE(ISNULL(c.RawEmp,''),'json'),
                   '","Status":"',  STRING_ESCAPE(ISNULL(c.RawStatus,''),'json'),
                   '","OT":"',      STRING_ESCAPE(ISNULL(c.RawOt,''),'json'), '"}')
        FROM #classified c
        WHERE c.RejectReason IS NOT NULL;

        SET @RowsRejected = @@ROWCOUNT;

        /* 3) ตัดซ้ำ (1 คน 1 วัน = แถวเดียว, ซ้ำเป๊ะเก็บแถวแรก) */
        IF OBJECT_ID('tempdb..#final') IS NOT NULL DROP TABLE #final;

        SELECT DateKey, MonthKey, EmployeeId, DepartmentId, StatusId, WorkHours, OtHours
        INTO #final
        FROM (
            SELECT c.*,
                   ROW_NUMBER() OVER (
                       PARTITION BY c.DateKey, c.EmployeeId
                       ORDER BY c.StgId
                   ) AS rn
            FROM #classified c
            WHERE c.RejectReason IS NULL
        ) t
        WHERE t.rn = 1;

        /* 4) โหลดเข้า Fact (ลบเดือนเดิมก่อน) */
        DELETE fa FROM core.FactAttendance fa
        WHERE fa.MonthKey IN (SELECT DISTINCT MonthKey FROM #final);

        INSERT INTO core.FactAttendance
            (DateKey, MonthKey, EmployeeId, DepartmentId, StatusId, WorkHours, OtHours, SourceRunId)
        SELECT DateKey, MonthKey, EmployeeId, DepartmentId, StatusId, WorkHours, OtHours, @RunId
        FROM #final;

        SET @RowsWritten = @@ROWCOUNT;

        UPDATE stg.AttendanceRaw SET IsProcessed = 1 WHERE RunId = @RunId;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO


/* =============================================================
   KPI : ATTENDANCE_RATE = present days / working days x 100
   working day = สถานะที่ IsWorkingDay=1 (ไม่นับวันหยุด)
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_CalcKpi_AttendanceRate
    @MonthKey INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @KpiId INT = (SELECT KpiId FROM meta.KpiDefinition WHERE KpiCode = 'ATTENDANCE_RATE');
    IF @KpiId IS NULL RETURN;

    ;WITH base AS (
        SELECT
            f.DepartmentId,
            SUM(CASE WHEN s.CountsAsPresent = 1 THEN 1 ELSE 0 END) AS PresentDays,
            SUM(CASE WHEN s.IsWorkingDay    = 1 THEN 1 ELSE 0 END) AS WorkingDays
        FROM core.FactAttendance f
        JOIN core.DimAttendanceStatus s ON s.StatusId = f.StatusId
        WHERE f.MonthKey = @MonthKey
        GROUP BY f.DepartmentId
    )
    INSERT INTO #KpiResult (KpiId, DepartmentId, ActualValue, Numerator, Denominator)
    SELECT @KpiId, DepartmentId,
           PresentDays * 100.0 / NULLIF(WorkingDays, 0), PresentDays, WorkingDays
    FROM base
    WHERE WorkingDays > 0;

    -- ระดับรวม
    INSERT INTO #KpiResult (KpiId, DepartmentId, ActualValue, Numerator, Denominator)
    SELECT @KpiId, -99,
           SUM(CASE WHEN s.CountsAsPresent = 1 THEN 1 ELSE 0 END) * 100.0
             / NULLIF(SUM(CASE WHEN s.IsWorkingDay = 1 THEN 1 ELSE 0 END), 0),
           SUM(CASE WHEN s.CountsAsPresent = 1 THEN 1 ELSE 0 END),
           SUM(CASE WHEN s.IsWorkingDay = 1 THEN 1 ELSE 0 END)
    FROM core.FactAttendance f
    JOIN core.DimAttendanceStatus s ON s.StatusId = f.StatusId
    WHERE f.MonthKey = @MonthKey
    HAVING SUM(CASE WHEN s.IsWorkingDay = 1 THEN 1 ELSE 0 END) > 0;
END
GO


/* =============================================================
   KPI : OVERTIME_HRS = SUM(OtHours) ต่อแผนก
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_CalcKpi_OvertimeHours
    @MonthKey INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @KpiId INT = (SELECT KpiId FROM meta.KpiDefinition WHERE KpiCode = 'OVERTIME_HRS');
    IF @KpiId IS NULL RETURN;

    INSERT INTO #KpiResult (KpiId, DepartmentId, ActualValue, Numerator, Denominator)
    SELECT @KpiId, f.DepartmentId, SUM(f.OtHours), SUM(f.OtHours), NULL
    FROM core.FactAttendance f
    WHERE f.MonthKey = @MonthKey
    GROUP BY f.DepartmentId;

    INSERT INTO #KpiResult (KpiId, DepartmentId, ActualValue, Numerator, Denominator)
    SELECT @KpiId, -99, SUM(f.OtHours), SUM(f.OtHours), NULL
    FROM core.FactAttendance f
    WHERE f.MonthKey = @MonthKey
    HAVING COUNT(*) > 0;
END
GO


/* =============================================================
   KPI : ABSENCE_RATE = absence days / working days x 100
   ============================================================= */
CREATE OR ALTER PROCEDURE core.usp_CalcKpi_AbsenceRate
    @MonthKey INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @KpiId INT = (SELECT KpiId FROM meta.KpiDefinition WHERE KpiCode = 'ABSENCE_RATE');
    IF @KpiId IS NULL RETURN;

    ;WITH base AS (
        SELECT
            f.DepartmentId,
            SUM(CASE WHEN s.CountsAsAbsence = 1 THEN 1 ELSE 0 END) AS AbsenceDays,
            SUM(CASE WHEN s.IsWorkingDay    = 1 THEN 1 ELSE 0 END) AS WorkingDays
        FROM core.FactAttendance f
        JOIN core.DimAttendanceStatus s ON s.StatusId = f.StatusId
        WHERE f.MonthKey = @MonthKey
        GROUP BY f.DepartmentId
    )
    INSERT INTO #KpiResult (KpiId, DepartmentId, ActualValue, Numerator, Denominator)
    SELECT @KpiId, DepartmentId,
           AbsenceDays * 100.0 / NULLIF(WorkingDays, 0), AbsenceDays, WorkingDays
    FROM base
    WHERE WorkingDays > 0;

    INSERT INTO #KpiResult (KpiId, DepartmentId, ActualValue, Numerator, Denominator)
    SELECT @KpiId, -99,
           SUM(CASE WHEN s.CountsAsAbsence = 1 THEN 1 ELSE 0 END) * 100.0
             / NULLIF(SUM(CASE WHEN s.IsWorkingDay = 1 THEN 1 ELSE 0 END), 0),
           SUM(CASE WHEN s.CountsAsAbsence = 1 THEN 1 ELSE 0 END),
           SUM(CASE WHEN s.IsWorkingDay = 1 THEN 1 ELSE 0 END)
    FROM core.FactAttendance f
    JOIN core.DimAttendanceStatus s ON s.StatusId = f.StatusId
    WHERE f.MonthKey = @MonthKey
    HAVING SUM(CASE WHEN s.IsWorkingDay = 1 THEN 1 ELSE 0 END) > 0;
END
GO

/* -------------------------------------------------------------
   ขยาย usp_RunKpi_AllMonths ให้รวมเดือนจาก FactAttendance ด้วย
   (เดิมดูแค่ production/cost/downtime)
   ------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE core.usp_RunKpi_AllMonths
    @TriggeredBy NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @m INT;
    DECLARE m_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT DISTINCT MonthKey FROM core.FactProduction
        UNION SELECT DISTINCT MonthKey FROM core.FactCost
        UNION SELECT DISTINCT MonthKey FROM core.FactDowntime
        UNION SELECT DISTINCT MonthKey FROM core.FactAttendance
        ORDER BY 1;
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
