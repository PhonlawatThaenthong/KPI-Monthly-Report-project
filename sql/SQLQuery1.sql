USE KpiMonthlyReport;

;WITH src AS (
    SELECT AliasRaw, DeptCode FROM (VALUES
        (N'Line A','LINE_A'),(N'line a','LINE_A'),(N'LINE-A','LINE_A'),
        (N'LineA','LINE_A'),(N'L-A','LINE_A'),
        (N'Line B','LINE_B'),(N'line b','LINE_B'),(N'LINE-B','LINE_B'),
        (N'LineB','LINE_B'),(N'L-B','LINE_B'),
        (N'Line C','LINE_C'),(N'line c','LINE_C'),(N'LINE-C','LINE_C'),
        (N'LineC','LINE_C'),(N'L-C','LINE_C'),
        (N'QC','QC'),(N'Q.C.','QC'),(N'Quality','QC'),
        (N'MA','MAINT'),(N'Maint','MAINT'),(N'ซ่อมบำรุง','MAINT')
    ) v (AliasRaw, DeptCode)
),
normalized AS (
    SELECT
        core.fn_NormalizeText(s.AliasRaw) AS AliasText,
        d.DepartmentId,
        ROW_NUMBER() OVER (
            PARTITION BY core.fn_NormalizeText(s.AliasRaw)
            ORDER BY d.DepartmentId
        ) AS rn
    FROM src s
    JOIN core.DimDepartment d ON d.DepartmentCode = s.DeptCode
)
INSERT INTO core.DepartmentAlias (AliasText, DepartmentId)
SELECT AliasText, DepartmentId
FROM normalized
WHERE rn = 1                             -- เอาเฉพาะตัวแรกของแต่ละ AliasText
  AND NOT EXISTS (
      SELECT 1 FROM core.DepartmentAlias a WHERE a.AliasText = normalized.AliasText
  );

-- ตรวจผล
SELECT COUNT(*) AS AliasCount FROM core.DepartmentAlias;
SELECT AliasText, DepartmentId FROM core.DepartmentAlias ORDER BY DepartmentId, AliasText;