/* =============================================================
   10_verify_etl.sql
   Purpose : ตรวจว่า ETL ทำงานถูกต้อง รันหลัง core.usp_RunEtl_Production
   ไม่แก้ไขข้อมูล รันซ้ำได้ตลอด
   ============================================================= */

USE KpiMonthlyReport;
GO

/* ---------- 1) จำนวนแถวต้องสมดุล ----------
   Unaccounted ต้องเป็น 0 ถ้าไม่ใช่ = มีแถวหายไปเงียบ ๆ
   หมายเหตุ: Written < Read เป็นเรื่องปกติ เพราะ
     - แถวซ้ำถูกตัดออก
     - หลายแถวถูกรวมเป็นแถวเดียวตอน GROUP BY
   จึงต้องดูข้อ 2 ประกอบ                                        */
SELECT TOP 5
    RunId, JobName, MonthKey, Status,
    RowsRead, RowsWritten, RowsRejected, DurationSec,
    StartedAt, ErrorMessage
FROM meta.EtlRunLog
ORDER BY RunId DESC;
GO

/* ---------- 2) เหตุผลการ reject ----------
   เทียบกับที่สำรวจไว้:  NEGATIVE_QTY 25 | DEFECT_GT_PRODUCED 45     */
SELECT RejectReason, COUNT(*) AS Cnt
FROM meta.DataRejectLog
WHERE RunId = (SELECT MAX(RunId) FROM meta.EtlRunLog)
GROUP BY RejectReason
ORDER BY Cnt DESC;
GO

/* ---------- 3) ดูตัวอย่างแถวที่ถูก reject จริง ---------- */
SELECT TOP 10 RejectReason, RawPayload
FROM meta.DataRejectLog
WHERE RunId = (SELECT MAX(RunId) FROM meta.EtlRunLog)
ORDER BY RejectId DESC;
GO

/* ---------- 4) ข้อมูล UNKNOWN ต้องไม่มากผิดปกติ ----------
   คาดหวัง: Unknown Dept ~1-2% | Unknown Product ~0.8%           */
SELECT
    COUNT(*)                                                    AS TotalRows,
    SUM(CASE WHEN DepartmentId = -1 THEN 1 ELSE 0 END)          AS UnknownDept,
    SUM(CASE WHEN ProductId    = -1 THEN 1 ELSE 0 END)          AS UnknownProduct,
    CAST(100.0 * SUM(CASE WHEN DepartmentId = -1 THEN 1 ELSE 0 END)
         / NULLIF(COUNT(*), 0) AS DECIMAL(5,2))                 AS UnknownDeptPct
FROM core.FactProduction;
GO

/* ---------- 5) กระจายตามเดือน ดูว่าครบ 18 เดือนไหม ---------- */
SELECT
    MonthKey,
    COUNT(*)            AS Rows,
    SUM(QtyProduced)    AS TotalProduced,
    SUM(QtyDefect)      AS TotalDefect,
    CAST(100.0 * SUM(QtyDefect) / NULLIF(SUM(QtyProduced), 0) AS DECIMAL(6,3)) AS DefectPct
FROM core.FactProduction
GROUP BY MonthKey
ORDER BY MonthKey;
GO

/* ---------- 6) กระจายตามแผนก ---------- */
SELECT
    d.DepartmentCode,
    d.DepartmentName,
    COUNT(*)         AS Rows,
    SUM(f.QtyProduced) AS TotalProduced
FROM core.FactProduction f
JOIN core.DimDepartment d ON d.DepartmentId = f.DepartmentId
GROUP BY d.DepartmentCode, d.DepartmentName
ORDER BY TotalProduced DESC;
GO

/* ---------- 7) ตรวจวันที่ที่แปลงไม่ได้ (ควรเป็น 0 แถว) ---------- */
SELECT COUNT(*) AS UnparsedDates
FROM stg.ProductionRaw
WHERE core.fn_ParseDate(ProdDate) IS NULL;
GO

/* =============================================================
   บททดสอบสำคัญที่สุด : IDEMPOTENCY
   รันชุดนี้ -> รัน ETL ซ้ำ -> รันชุดนี้อีกครั้ง
   ตัวเลขทั้ง 3 ค่าต้องเท่าเดิมเป๊ะ
   ============================================================= */
SELECT
    COUNT(*)          AS RowCnt,
    SUM(QtyProduced)  AS SumProduced,
    SUM(QtyDefect)    AS SumDefect
FROM core.FactProduction;
GO
