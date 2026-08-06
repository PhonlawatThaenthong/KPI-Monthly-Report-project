/* =============================================================
   16_hr_seed.sql
   Purpose : seed พนักงาน, สถานะการมางาน, และนิยาม KPI บุคลากร 3 ตัว
   Idempotent : YES
   ต้องรัน 15_hr_tables.sql ก่อน
   ============================================================= */

USE KpiMonthlyReport;
GO

/* ---------- 1) สถานะการมางาน ---------- */
MERGE core.DimAttendanceStatus AS t
USING (VALUES
    --  code       name              present absence workingday
    ('PRESENT',  N'Present',        1, 0, 1),
    ('LATE',     N'Late',           1, 0, 1),   -- มาสายยังนับว่ามา แต่แยกไว้ดูได้
    ('LEAVE',    N'Annual Leave',   0, 1, 1),
    ('SICK',     N'Sick Leave',     0, 1, 1),
    ('ABSENT',   N'Absent',         0, 1, 1),
    ('HOLIDAY',  N'Holiday',        0, 0, 0)    -- วันหยุด ไม่นับเป็นตัวหาร
) AS s (StatusCode, StatusName, CountsAsPresent, CountsAsAbsence, IsWorkingDay)
ON t.StatusCode = s.StatusCode
WHEN MATCHED THEN UPDATE SET
    t.StatusName = s.StatusName,
    t.CountsAsPresent = s.CountsAsPresent,
    t.CountsAsAbsence = s.CountsAsAbsence,
    t.IsWorkingDay = s.IsWorkingDay
WHEN NOT MATCHED THEN
    INSERT (StatusCode, StatusName, CountsAsPresent, CountsAsAbsence, IsWorkingDay)
    VALUES (s.StatusCode, s.StatusName, s.CountsAsPresent, s.CountsAsAbsence, s.IsWorkingDay);
GO

/* ---------- 2) พนักงานตัวอย่าง (กระจายตามแผนกผลิต) ----------
   สร้าง 8-10 คนต่อแผนกผลิต + support ให้พอมีข้อมูลคำนวณ
   ใช้ generator ฝั่ง Python สร้างจริง แต่ seed dim ไว้ที่นี่เพื่อความ reproducible */
;WITH nums AS (
    SELECT TOP (40) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_objects
),
emp AS (
    SELECT
        n.n,
        'EMP-' + RIGHT('0000' + CAST(n.n AS VARCHAR(4)), 4) AS EmployeeCode,
        d.DepartmentId,
        d.DepartmentCode
    FROM nums n
    CROSS APPLY (
        -- กระจายพนักงานเวียนตามแผนก
        SELECT DepartmentId, DepartmentCode
        FROM (
            SELECT DepartmentId, DepartmentCode,
                   ROW_NUMBER() OVER (ORDER BY DepartmentId) AS rn
            FROM core.DimDepartment
            WHERE DepartmentId > 0 AND IsActive = 1
        ) dd
        WHERE dd.rn = ((n.n - 1) % 5) + 1
    ) d
)
MERGE core.DimEmployee AS t
USING (
    SELECT
        EmployeeCode,
        N'Employee ' + CAST(n AS NVARCHAR(10)) AS EmployeeName,
        DepartmentId,
        N'Operator' AS Position,
        DATEADD(DAY, -(n * 30), CAST('2024-12-31' AS DATE)) AS HireDate
    FROM emp
) AS s (EmployeeCode, EmployeeName, DepartmentId, Position, HireDate)
ON t.EmployeeCode = s.EmployeeCode
WHEN NOT MATCHED THEN
    INSERT (EmployeeCode, EmployeeName, DepartmentId, Position, HireDate)
    VALUES (s.EmployeeCode, s.EmployeeName, s.DepartmentId, s.Position, s.HireDate);
GO

/* ---------- 3) นิยาม KPI บุคลากร 3 ตัว ---------- */
MERGE meta.KpiDefinition AS t
USING (VALUES
    ('ATTENDANCE_RATE', N'Attendance Rate', N'อัตราการมางาน',  N'HR', N'%',   2, 'H',
     'core.usp_CalcKpi_AttendanceRate', N'Present Days / Working Days x 100', 60),

    ('OVERTIME_HRS',    N'Overtime Hours',  N'ชั่วโมงล่วงเวลา', N'HR', N'hrs', 1, 'L',
     'core.usp_CalcKpi_OvertimeHours',  N'SUM(OT Hours)', 70),

    ('ABSENCE_RATE',    N'Absence Rate',    N'อัตราการขาดงาน',  N'HR', N'%',   2, 'L',
     'core.usp_CalcKpi_AbsenceRate',    N'Absence Days / Working Days x 100', 80)
) AS s (KpiCode, KpiName, KpiNameTh, CategoryName, Unit, DecimalPlaces, Direction,
        CalcProcName, FormulaText, SortOrder)
ON t.KpiCode = s.KpiCode
WHEN MATCHED THEN UPDATE SET
    t.KpiName = s.KpiName, t.KpiNameTh = s.KpiNameTh, t.CategoryName = s.CategoryName,
    t.Unit = s.Unit, t.DecimalPlaces = s.DecimalPlaces, t.Direction = s.Direction,
    t.CalcProcName = s.CalcProcName, t.FormulaText = s.FormulaText, t.SortOrder = s.SortOrder,
    t.UpdatedAt = SYSDATETIME(), t.UpdatedBy = N'HR_SEED'
WHEN NOT MATCHED THEN
    INSERT (KpiCode, KpiName, KpiNameTh, CategoryName, Unit, DecimalPlaces,
            Direction, CalcProcName, FormulaText, SortOrder, CreatedBy)
    VALUES (s.KpiCode, s.KpiName, s.KpiNameTh, s.CategoryName, s.Unit, s.DecimalPlaces,
            s.Direction, s.CalcProcName, s.FormulaText, s.SortOrder, N'HR_SEED');
GO

/* ---------- 4) Target ของ KPI บุคลากร ทุกเดือน ---------- */
;WITH m AS (
    SELECT DISTINCT MonthKey FROM core.DimDate
),
tgt AS (
    SELECT k.KpiId, m.MonthKey, v.TargetValue
    FROM m
    CROSS JOIN (VALUES
        ('ATTENDANCE_RATE', 95.0000),
        ('OVERTIME_HRS',   200.0000),
        ('ABSENCE_RATE',     5.0000)
    ) v (KpiCode, TargetValue)
    JOIN meta.KpiDefinition k ON k.KpiCode = v.KpiCode
)
MERGE meta.KpiTarget AS t
USING tgt AS s
ON t.KpiId = s.KpiId AND t.MonthKey = s.MonthKey AND t.DepartmentId IS NULL
WHEN NOT MATCHED THEN
    INSERT (KpiId, MonthKey, DepartmentId, TargetValue, CreatedBy)
    VALUES (s.KpiId, s.MonthKey, NULL, s.TargetValue, N'HR_SEED');
GO

/* ---------- ตรวจผล ---------- */
SELECT 'DimEmployee' AS TableName, COUNT(*) AS Rows FROM core.DimEmployee
UNION ALL SELECT 'DimAttendanceStatus', COUNT(*) FROM core.DimAttendanceStatus
UNION ALL SELECT 'HR KpiDefinition', COUNT(*) FROM meta.KpiDefinition WHERE CategoryName = 'HR';
GO
