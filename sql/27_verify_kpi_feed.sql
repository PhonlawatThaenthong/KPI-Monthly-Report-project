/* =============================================================
   27_verify_kpi_feed.sql
   Purpose : ตรวจว่าระบบหลังเปลี่ยนมาใช้ KPI feed ทำงานถูกต้องจริง
   Idempotent : YES (อ่านอย่างเดียว ไม่แก้ข้อมูล ยกเว้นส่วน D ที่บอกไว้ชัด)

   วิธีใช้
   ---------------------------------------------------------------
   ส่วน A : รันทันทีหลังรัน 26 (ยังไม่ต้องมีข้อมูล)
   ส่วน B : รันหลัง  KpiReport.Etl.exe run-all  รอบแรก
   ส่วน C : ตรวจความถูกต้องของตัวเลข
   ส่วน D : ตรวจ idempotency (ต้องรัน ETL ซ้ำอีกรอบก่อน)

   ทุกเช็คคืนคอลัมน์ Result = PASS / FAIL อ่านแค่คอลัมน์นั้นพอ
   ส่วน B/D ตั้งสมมติฐานว่าโหลด mock ชุดนี้เข้าฐานข้อมูลที่ยังว่าง
   ถ้าฐานข้อมูลมีข้อมูลเก่าปนอยู่ ให้ล้างด้วย core.usp_PurgeMonth ก่อน
   ตัวเลขที่คาดหวังอิงชุด mock ที่ให้มา (mock-data/kpi-feed 18 เดือน)
   ถ้าสร้าง mock ใหม่ด้วย seed อื่น ตัวเลขในส่วน B จะเปลี่ยน
   ============================================================= */

USE KpiMonthlyReport;
GO

PRINT '=============== ส่วน A : โครงสร้างฐานข้อมูล ===============';
GO

/* A1 : ของใหม่ต้องมีครบ */
SELECT 'A1 ของใหม่ครบ' AS Check_,
       CASE WHEN COUNT(*) = 5 THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(*) AS Found, 5 AS Expected
FROM (VALUES
    ('stg.KpiFeedRaw'), ('core.usp_Transform_KpiFeed'),
    ('core.usp_RefreshKpi_Derived'), ('rpt.vw_ValidMonth'), ('core.usp_PurgeMonth')
) o(Name)
WHERE OBJECT_ID(o.Name) IS NOT NULL;

/* A2 : ของเก่าต้องไม่เหลือเลย */
SELECT 'A2 ของเก่าถูกถอดหมด' AS Check_,
       CASE WHEN COUNT(*) = 0 THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(*) AS StillExists, 0 AS Expected
FROM (VALUES
    ('core.FactAttendance'), ('stg.AttendanceRaw'), ('core.DimEmployee'),
    ('core.DimAttendanceStatus'), ('core.EmployeeAlias'),
    ('core.usp_RunKpi_Monthly'), ('core.usp_RunKpi_AllMonths'),
    ('core.usp_Transform_Attendance'),
    ('core.usp_CalcKpi_AttendanceRate'), ('core.usp_CalcKpi_OvertimeHours'),
    ('core.usp_CalcKpi_AbsenceRate')
) o(Name)
WHERE OBJECT_ID(o.Name) IS NOT NULL;

/* A3 : นิยาม KPI ต้องเหลือ 3 ตัว ผูกรหัสต้นทางครบ และไม่มี CalcProcName */
SELECT 'A3 นิยาม KPI' AS Check_,
       CASE WHEN COUNT(*) = 3
             AND SUM(CASE WHEN SourceMetricCode IS NULL THEN 1 ELSE 0 END) = 0
             AND SUM(CASE WHEN CalcProcName IS NOT NULL THEN 1 ELSE 0 END) = 0
            THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(*) AS KpiCount
FROM meta.KpiDefinition
WHERE IsActive = 1;

SELECT KpiCode, SourceMetricCode, Direction, Unit, CalcProcName, IsActive
FROM meta.KpiDefinition ORDER BY SortOrder;
GO


PRINT '=============== ส่วน B : ผลการโหลดข้อมูล ===============';
GO

/* B1 : จำนวนแถวที่ต้นทางส่งมา (mock 18 เดือน = 313 แถว) */
SELECT 'B1 แถวใน staging' AS Check_,
       CASE WHEN COUNT(*) = 313 THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(*) AS Found, 313 AS Expected
FROM stg.KpiFeedRaw;

/* B2 : แถวที่โหลดเข้า fact สำเร็จ (313 - 2 แถวที่ตั้งใจให้เสีย) */
SELECT 'B2 แถวใน FactKpiMonthly' AS Check_,
       CASE WHEN COUNT(*) = 311 THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(*) AS Found, 311 AS Expected
FROM core.FactKpiMonthly;

/* B3 : แถวที่ตั้งใจให้เสีย ต้องถูกตัดออกพร้อมเหตุผลที่ถูกต้อง
        UNKNOWN_KPI 1 แถว (HEADCOUNT_TOTAL ใน 2025-11)
        MISSING_ACTUAL_VALUE 1 แถว ("N/A" ใน 2025-08) */
SELECT 'B3 การตัดแถวเสีย' AS Check_,
       CASE WHEN SUM(CASE WHEN RejectReason = 'UNKNOWN_KPI' THEN 1 ELSE 0 END) = 1
             AND SUM(CASE WHEN RejectReason = 'MISSING_ACTUAL_VALUE' THEN 1 ELSE 0 END) = 1
             AND COUNT(*) = 2
            THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(*) AS TotalRejected
FROM meta.DataRejectLog
WHERE SourceTable = 'stg.KpiFeedRaw';

SELECT RejectReason, COUNT(*) AS Rows_, MIN(RawPayload) AS ExampleRow
FROM meta.DataRejectLog
WHERE SourceTable = 'stg.KpiFeedRaw'
GROUP BY RejectReason;

/* B4 : แผนกที่สะกดไม่ตรงต้องถูก DepartmentAlias จับคู่ได้
        2025-03 ส่ง 'L-A' กับ 'line b' มา ต้องกลายเป็น LINE_A / LINE_B
        ถ้า FAIL แปลว่า alias ไม่ทำงาน -> จะเห็นเดือนนั้นมีแค่ 3 แผนก */
SELECT 'B4 จับคู่ชื่อแผนกที่สะกดไม่ตรง' AS Check_,
       CASE WHEN COUNT(DISTINCT DepartmentId) = 6 THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(DISTINCT DepartmentId) AS DeptFound, 6 AS Expected   -- 5 แผนก + ALL(-99)
FROM core.FactKpiMonthly WHERE MonthKey = 202503;

/* B5 : ไม่มีแถวไหนตกไปอยู่แผนก Unknown (-1) */
SELECT 'B5 ไม่มีแถวแผนก Unknown' AS Check_,
       CASE WHEN COUNT(*) = 0 THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(*) AS Found
FROM core.FactKpiMonthly WHERE DepartmentId = -1;

/* B6 : เดือนผี — 2025-01 ต้นทางส่งมาแค่ KPI เดียว ต้องถูกกรองออก
        ที่เหลือ 17 เดือนต้องใช้ได้ */
SELECT 'B6 กรองเดือนผี' AS Check_,
       CASE WHEN (SELECT COUNT(*) FROM rpt.vw_ValidMonth) = 17
             AND NOT EXISTS (SELECT 1 FROM rpt.vw_ValidMonth WHERE MonthKey = 202501)
            THEN 'PASS' ELSE 'FAIL' END AS Result,
       (SELECT COUNT(*) FROM rpt.vw_ValidMonth) AS ValidMonths, 17 AS Expected;

/* B8 : ต้องไม่มีค่า KPI ที่ไม่ได้มาจาก feed ค้างอยู่
        ถ้า FAIL แปลว่าค่าเก่าจากเครื่องคำนวณเดิมยังปนอยู่ในตาราง
        แก้ด้วยการรัน 26 ใหม่ (ส่วน 6.5 ล้างให้) แล้วรัน ETL อีกรอบ */
SELECT 'B8 ไม่มีค่าเก่าจากเครื่องคำนวณเดิม' AS Check_,
       CASE WHEN COUNT(*) = 0 THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(*) AS StaleRows
FROM core.FactKpiMonthly f
LEFT JOIN meta.EtlRunLog r ON r.RunId = f.SourceRunId
WHERE r.RunId IS NULL OR r.JobName <> N'ETL_KpiFeed';

/* ถ้า B2 หรือ B8 FAIL : ดูว่าแถวส่วนเกินมาจากเดือนไหนและงานอะไร */
;WITH feed_month AS (
    SELECT DISTINCT TRY_CONVERT(INT, REPLACE(REPLACE(MonthText, '-', ''), '/', '')) AS MonthKey
    FROM stg.KpiFeedRaw
)
SELECT f.MonthKey,
       ISNULL(r.JobName, N'(ไม่มี run)') AS FromJob,
       CASE WHEN m.MonthKey IS NULL THEN N'ไม่มีใน feed' ELSE N'มีใน feed' END AS InFeed,
       COUNT(*) AS Rows_
FROM core.FactKpiMonthly f
LEFT JOIN meta.EtlRunLog r ON r.RunId = f.SourceRunId
LEFT JOIN feed_month m     ON m.MonthKey = f.MonthKey
GROUP BY f.MonthKey, r.JobName,
         CASE WHEN m.MonthKey IS NULL THEN N'ไม่มีใน feed' ELSE N'มีใน feed' END
ORDER BY f.MonthKey;

/* B7 : ETL log ต้องขึ้น SUCCESS ไม่มี RUNNING ค้าง */
SELECT 'B7 ETL log' AS Check_,
       CASE WHEN SUM(CASE WHEN Status <> 'SUCCESS' THEN 1 ELSE 0 END) = 0
            THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(*) AS Runs
FROM meta.EtlRunLog WHERE JobName = 'ETL_KpiFeed';

SELECT TOP 5 RunId, JobName, Status, RowsRead, RowsWritten, RowsRejected, DurationSec, StartedAt
FROM meta.EtlRunLog ORDER BY RunId DESC;
GO


PRINT '=============== ส่วน C : ความถูกต้องของตัวเลข ===============';
GO

/* C1 : ระบบต้อง "ไม่แปลงค่า" — ค่าใน fact ต้องเท่ากับที่ต้นทางส่งมาเป๊ะ
        เทียบ fact กับ staging ทีละแถว ต่างเมื่อไรคือระบบไปคำนวณเอง
        นี่คือเช็คที่สำคัญที่สุดของสถาปัตยกรรมใหม่ */
;WITH src AS (
    SELECT TRY_CONVERT(INT, REPLACE(REPLACE(r.MonthText, '-', ''), '/', '')) AS MonthKey,
           k.KpiId,
           CASE WHEN core.fn_NormalizeText(r.DepartmentText) IN (N'all', N'99', N'company', N'total')
                THEN -99 ELSE COALESCE(d.DepartmentId, a.DepartmentId) END   AS DepartmentId,
           core.fn_ParseDecimal(r.ActualValueText)                           AS SrcActual
    FROM stg.KpiFeedRaw r
    LEFT JOIN meta.KpiDefinition k
           ON core.fn_NormalizeText(k.SourceMetricCode) = core.fn_NormalizeText(r.KpiCodeText)
    LEFT JOIN core.DimDepartment d
           ON core.fn_NormalizeText(d.DepartmentCode) = core.fn_NormalizeText(r.DepartmentText)
          AND d.DepartmentId > 0
    LEFT JOIN core.DepartmentAlias a
           ON a.AliasText = core.fn_NormalizeText(r.DepartmentText)
)
SELECT 'C1 ค่าไม่ถูกแปลงระหว่างทาง' AS Check_,
       CASE WHEN COUNT(*) = 0 THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(*) AS Mismatches
FROM core.FactKpiMonthly f
JOIN src s ON s.MonthKey = f.MonthKey
          AND s.KpiId = f.KpiId
          AND s.DepartmentId = f.DepartmentId
WHERE ABS(f.ActualValue - s.SrcActual) > 0.0001;

/* C2 : StatusFlag ต้องตรงกับทิศทางของ KPI เทียบเป้า
        H (ยิ่งมากยิ่งดี) : >= เป้า = GREEN, >= 90% ของเป้า = YELLOW
        L (ยิ่งน้อยยิ่งดี): <= เป้า = GREEN, <= 110% ของเป้า = YELLOW */
SELECT 'C2 สีสถานะถูกต้อง' AS Check_,
       CASE WHEN COUNT(*) = 0 THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(*) AS WrongRows
FROM core.FactKpiMonthly f
JOIN meta.KpiDefinition k ON k.KpiId = f.KpiId
WHERE f.ActualValue IS NOT NULL AND f.TargetValue IS NOT NULL
  AND f.StatusFlag <> CASE
        WHEN k.Direction = 'H' THEN
            CASE WHEN f.ActualValue >= f.TargetValue        THEN 'GREEN'
                 WHEN f.ActualValue >= f.TargetValue * 0.90 THEN 'YELLOW' ELSE 'RED' END
        ELSE
            CASE WHEN f.ActualValue <= f.TargetValue        THEN 'GREEN'
                 WHEN f.ActualValue <= f.TargetValue * 1.10 THEN 'YELLOW' ELSE 'RED' END
      END;

/* C3 : PrevMonthValue ต้องเท่ากับค่าจริงของเดือนก่อนหน้าที่มีข้อมูล */
;WITH lagged AS (
    SELECT KpiFactKey, PrevMonthValue,
           LAG(ActualValue) OVER (PARTITION BY KpiId, DepartmentId ORDER BY MonthKey) AS ShouldBe
    FROM core.FactKpiMonthly
)
SELECT 'C3 ค่าเดือนก่อนถูกต้อง' AS Check_,
       CASE WHEN COUNT(*) = 0 THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(*) AS WrongRows
FROM lagged
WHERE ISNULL(PrevMonthValue, -999999) <> ISNULL(ShouldBe, -999999);

/* C4 : ระดับบริษัท (ALL) ต้องสอดคล้องกับตัวตั้ง/ตัวหารรวมของทุกแผนก
        ตรวจเฉพาะ KPI ที่เป็นอัตรา ยอมให้ต่างได้ 0.05 จากการปัดเศษฝั่งต้นทาง */
;WITH dept AS (
    SELECT MonthKey, KpiId, SUM(Numerator) AS N, SUM(Denominator) AS D
    FROM core.FactKpiMonthly
    WHERE DepartmentId <> -99 AND Denominator IS NOT NULL
    GROUP BY MonthKey, KpiId
)
SELECT 'C4 ค่ารวมบริษัทสอดคล้องกับรายแผนก' AS Check_,
       CASE WHEN COUNT(*) = 0 THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(*) AS InconsistentRows
FROM core.FactKpiMonthly a
JOIN dept t ON t.MonthKey = a.MonthKey AND t.KpiId = a.KpiId
WHERE a.DepartmentId = -99
  AND a.MonthKey <> 202508              -- เดือนที่จงใจให้ค่าหาย 1 แผนก
  AND ABS(a.ActualValue - (t.N * 100.0 / NULLIF(t.D, 0))) > 0.05;

/* C5 : ไม่มีค่าซ้ำในคีย์ธรรมชาติ (UNIQUE ควรกันให้อยู่แล้ว แต่ตรวจซ้ำ) */
SELECT 'C5 ไม่มีแถวซ้ำ' AS Check_,
       CASE WHEN COUNT(*) = 0 THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(*) AS DuplicateKeys
FROM (
    SELECT MonthKey, KpiId, DepartmentId
    FROM core.FactKpiMonthly
    GROUP BY MonthKey, KpiId, DepartmentId
    HAVING COUNT(*) > 1
) d;

/* C6 : เป้าหมายที่ต้นทางส่งมาถูกเก็บลง meta.KpiTarget แล้ว */
SELECT 'C6 เป้าหมายจาก feed' AS Check_,
       CASE WHEN COUNT(*) > 0 THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(*) AS TargetRowsFromFeed
FROM meta.KpiTarget WHERE CreatedBy = N'KPI_FEED';

/* C7 : สิ่งที่หน้า Dashboard จะเห็นจริง — ดูด้วยตาอีกชั้น */
EXEC rpt.usp_GetKpiDashboard @MonthKey = 202606, @DepartmentId = NULL;
EXEC rpt.usp_GetKpiTrend @KpiCode = 'ATTENDANCE_RATE', @DepartmentId = -99, @MonthsBack = 12;
GO


PRINT '=============== ส่วน D : idempotency (รัน ETL ซ้ำก่อน) ===============';
GO

/* D1 : รัน KpiReport.Etl.exe run-all ซ้ำอีกรอบ แล้วรันเช็คนี้
        ไฟล์เดิมลายนิ้วมือเดิม -> ต้องถูกข้าม ไม่เข้า staging ซ้ำ
        จำนวนแถวใน fact ต้องเท่าเดิมเป๊ะ (311) */
SELECT 'D1 รันซ้ำแล้วข้อมูลไม่บาน' AS Check_,
       CASE WHEN (SELECT COUNT(*) FROM core.FactKpiMonthly) = 311
             AND (SELECT COUNT(*) FROM stg.KpiFeedRaw) = 313
            THEN 'PASS' ELSE 'FAIL' END AS Result,
       (SELECT COUNT(*) FROM core.FactKpiMonthly) AS FactRows,
       (SELECT COUNT(*) FROM stg.KpiFeedRaw) AS StagingRows;

/* D2 : ไฟล์ทุกชุดถูกบันทึกลายนิ้วมือไว้ (18 ไฟล์) */
SELECT 'D2 บันทึกลายนิ้วมือไฟล์' AS Check_,
       CASE WHEN COUNT(DISTINCT FileHash) = 18 THEN 'PASS' ELSE 'FAIL' END AS Result,
       COUNT(DISTINCT FileHash) AS Files, 18 AS Expected
FROM stg.FileLoadHistory
WHERE FileName LIKE 'KpiFeed[_]%';
GO
