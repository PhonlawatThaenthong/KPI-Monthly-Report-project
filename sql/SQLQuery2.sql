USE KpiMonthlyReport;

-- A) มี alias กี่แถว
SELECT COUNT(*) AS AliasCount FROM core.DepartmentAlias;

-- B) ดูค่าที่เก็บจริง เทียบความยาว
SELECT AliasText, DepartmentId, LEN(AliasText) AS Len
FROM core.DepartmentAlias
WHERE AliasText LIKE '%line%'
ORDER BY AliasText;

-- C) จับคู่ตรง ๆ ทีละตัว ดูว่าพังตรงไหน
DECLARE @norm NVARCHAR(200) = core.fn_NormalizeText(N'Line C');
SELECT
    @norm                          AS NormResult,
    LEN(@norm)                     AS NormLen,
    (SELECT DepartmentId FROM core.DepartmentAlias WHERE AliasText = @norm) AS FoundId,
    (SELECT DepartmentId FROM core.DepartmentAlias WHERE AliasText = N'linec') AS DirectId;