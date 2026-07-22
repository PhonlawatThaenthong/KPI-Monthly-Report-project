/* =============================================================
   07_seed.sql
   Purpose : ข้อมูลตั้งต้น - แผนก, alias, สินค้า, ประเภทต้นทุน,
             นิยาม KPI, ปฏิทิน, target
   Idempotent : YES (ใช้ MERGE / NOT EXISTS)
   ============================================================= */

USE KpiMonthlyReport;
GO

/* ---------- 1) ปฏิทิน 3 ปี ---------- */
EXEC core.usp_DimDate_Populate
     @StartDate = '2025-01-01',
     @EndDate   = '2027-12-31',
     @FiscalStartMonth = 10;
GO

/* ---------- 2) แผนก ---------- */
MERGE core.DimDepartment AS t
USING (VALUES
    ('LINE_A', N'Line A', N'ไลน์ผลิต A', 'PLANT1', N'สมชาย ก.'),
    ('LINE_B', N'Line B', N'ไลน์ผลิต B', 'PLANT1', N'สมหญิง ข.'),
    ('LINE_C', N'Line C', N'ไลน์ผลิต C', 'PLANT1', N'ประยุทธ ค.'),
    ('QC',     N'Quality Control', N'ควบคุมคุณภาพ', 'PLANT1', N'วิภา ง.'),
    ('MAINT',  N'Maintenance', N'ซ่อมบำรุง', 'PLANT1', N'อนันต์ จ.')
) AS s (DepartmentCode, DepartmentName, DepartmentNameTh, PlantCode, ManagerName)
ON t.DepartmentCode = s.DepartmentCode
WHEN NOT MATCHED THEN
    INSERT (DepartmentCode, DepartmentName, DepartmentNameTh, PlantCode, ManagerName)
    VALUES (s.DepartmentCode, s.DepartmentName, s.DepartmentNameTh, s.PlantCode, s.ManagerName);
GO

/* ---------- 3) Alias ของชื่อแผนก (จำลองข้อมูลสกปรก) ---------- */
;WITH src AS (
    SELECT * FROM (VALUES
        (N'Line A',  'LINE_A'), (N'line a',  'LINE_A'), (N'LINE-A', 'LINE_A'),
        (N'LineA',   'LINE_A'), (N'L-A',     'LINE_A'),
        (N'Line B',  'LINE_B'), (N'line b',  'LINE_B'), (N'LINE-B', 'LINE_B'),
        (N'LineB',   'LINE_B'), (N'L-B',     'LINE_B'),
        (N'Line C',  'LINE_C'), (N'line c',  'LINE_C'), (N'LINE-C', 'LINE_C'),
        (N'LineC',   'LINE_C'), (N'L-C',     'LINE_C'),
        (N'QC',      'QC'),     (N'Q.C.',    'QC'),     (N'Quality', 'QC'),
        (N'MA',      'MAINT'),  (N'Maint',   'MAINT'),  (N'ซ่อมบำรุง', 'MAINT')
    ) v (AliasRaw, DeptCode)
)
MERGE core.DepartmentAlias AS t
USING (
    SELECT core.fn_NormalizeText(s.AliasRaw) AS AliasText, d.DepartmentId
    FROM src s
    JOIN core.DimDepartment d ON d.DepartmentCode = s.DeptCode
) AS s
ON t.AliasText = s.AliasText
WHEN NOT MATCHED THEN
    INSERT (AliasText, DepartmentId) VALUES (s.AliasText, s.DepartmentId);
GO

/* ---------- 4) สินค้า ---------- */
MERGE core.DimProduct AS t
USING (VALUES
    ('P-1001', N'Bracket Type A', N'Bracket', 45.50,  N'pcs'),
    ('P-1002', N'Bracket Type B', N'Bracket', 52.00,  N'pcs'),
    ('P-2001', N'Housing Small',  N'Housing', 128.75, N'pcs'),
    ('P-2002', N'Housing Large',  N'Housing', 195.00, N'pcs'),
    ('P-3001', N'Shaft 120mm',    N'Shaft',   88.25,  N'pcs')
) AS s (ProductCode, ProductName, ProductGroup, StandardCost, UnitOfMeasure)
ON t.ProductCode = s.ProductCode
WHEN NOT MATCHED THEN
    INSERT (ProductCode, ProductName, ProductGroup, StandardCost, UnitOfMeasure)
    VALUES (s.ProductCode, s.ProductName, s.ProductGroup, s.StandardCost, s.UnitOfMeasure);
GO

/* ---------- 5) ประเภทต้นทุน ---------- */
MERGE core.DimCostType AS t
USING (VALUES
    ('MATERIAL', N'Material Cost',  1),
    ('LABOR',    N'Labor Cost',     1),
    ('OVERHEAD', N'Overhead Cost',  0),
    ('UTILITY',  N'Utility Cost',   1)
) AS s (CostTypeCode, CostTypeName, IsVariable)
ON t.CostTypeCode = s.CostTypeCode
WHEN NOT MATCHED THEN
    INSERT (CostTypeCode, CostTypeName, IsVariable)
    VALUES (s.CostTypeCode, s.CostTypeName, s.IsVariable);
GO

/* ---------- 6) นิยาม KPI 5 ตัว ---------- */
MERGE meta.KpiDefinition AS t
USING (VALUES
    ('PROD_OUTPUT',   N'Production Output',  N'ปริมาณการผลิต',      N'Production', N'pcs',  0, 'H',
     'core.usp_CalcKpi_ProductionOutput', N'SUM(QtyProduced)', 10),

    ('DEFECT_RATE',   N'Defect Rate',        N'อัตราของเสีย',        N'Quality',    N'%',    2, 'L',
     'core.usp_CalcKpi_DefectRate',       N'SUM(QtyDefect) / SUM(QtyProduced) x 100', 20),

    ('DOWNTIME_HRS',  N'Downtime Hours',     N'ชั่วโมงเครื่องหยุด',   N'Production', N'hrs',  1, 'L',
     'core.usp_CalcKpi_DowntimeHours',    N'SUM(DurationMinutes) / 60', 30),

    ('COST_PER_UNIT', N'Cost per Unit',      N'ต้นทุนต่อหน่วย',      N'Cost',       N'THB',  2, 'L',
     'core.usp_CalcKpi_CostPerUnit',      N'SUM(Cost Amount) / SUM(QtyGood)', 40),

    ('COST_DOWN_PCT', N'Cost Down',          N'ผลการลดต้นทุน',       N'Cost',       N'%',    2, 'H',
     'core.usp_CalcKpi_CostDown',         N'(Baseline - Actual) / Baseline x 100', 50)
) AS s (KpiCode, KpiName, KpiNameTh, CategoryName, Unit, DecimalPlaces, Direction,
        CalcProcName, FormulaText, SortOrder)
ON t.KpiCode = s.KpiCode
WHEN MATCHED THEN UPDATE SET
    t.KpiName       = s.KpiName,
    t.KpiNameTh     = s.KpiNameTh,
    t.CategoryName  = s.CategoryName,
    t.Unit          = s.Unit,
    t.DecimalPlaces = s.DecimalPlaces,
    t.Direction     = s.Direction,
    t.CalcProcName  = s.CalcProcName,
    t.FormulaText   = s.FormulaText,
    t.SortOrder     = s.SortOrder,
    t.UpdatedAt     = SYSDATETIME(),
    t.UpdatedBy     = N'SEED'
WHEN NOT MATCHED THEN
    INSERT (KpiCode, KpiName, KpiNameTh, CategoryName, Unit, DecimalPlaces,
            Direction, CalcProcName, FormulaText, SortOrder, CreatedBy)
    VALUES (s.KpiCode, s.KpiName, s.KpiNameTh, s.CategoryName, s.Unit, s.DecimalPlaces,
            s.Direction, s.CalcProcName, s.FormulaText, s.SortOrder, N'SEED');
GO

/* ---------- 7) Target ตัวอย่าง ปี 2026 ทุกเดือน (ระดับรวม) ---------- */
;WITH m AS (
    SELECT DISTINCT MonthKey FROM core.DimDate WHERE [Year] = 2026
),
tgt AS (
    SELECT k.KpiId, m.MonthKey, v.TargetValue, v.BaselineValue
    FROM m
    CROSS JOIN (VALUES
        ('PROD_OUTPUT',   50000.0000, NULL),
        ('DEFECT_RATE',       2.5000, NULL),
        ('DOWNTIME_HRS',     40.0000, NULL),
        ('COST_PER_UNIT',   120.0000, 135.0000),
        ('COST_DOWN_PCT',     5.0000, NULL)
    ) v (KpiCode, TargetValue, BaselineValue)
    JOIN meta.KpiDefinition k ON k.KpiCode = v.KpiCode
)
MERGE meta.KpiTarget AS t
USING tgt AS s
ON  t.KpiId = s.KpiId
AND t.MonthKey = s.MonthKey
AND t.DepartmentId IS NULL
WHEN NOT MATCHED THEN
    INSERT (KpiId, MonthKey, DepartmentId, TargetValue, BaselineValue, CreatedBy)
    VALUES (s.KpiId, s.MonthKey, NULL, s.TargetValue, s.BaselineValue, N'SEED');
GO

PRINT '>> Seed completed';
GO

/* -------------------------------------------------------------
   ตรวจผล seed
   ------------------------------------------------------------- */
SELECT 'DimDate'          AS TableName, COUNT(*) AS Rows FROM core.DimDate
UNION ALL SELECT 'DimDepartment',  COUNT(*) FROM core.DimDepartment
UNION ALL SELECT 'DepartmentAlias',COUNT(*) FROM core.DepartmentAlias
UNION ALL SELECT 'DimProduct',     COUNT(*) FROM core.DimProduct
UNION ALL SELECT 'DimCostType',    COUNT(*) FROM core.DimCostType
UNION ALL SELECT 'KpiDefinition',  COUNT(*) FROM meta.KpiDefinition
UNION ALL SELECT 'KpiTarget',      COUNT(*) FROM meta.KpiTarget;
GO
