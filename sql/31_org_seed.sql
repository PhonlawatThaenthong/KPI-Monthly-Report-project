/* =============================================================
   31_org_seed.sql   *** ไฟล์นี้ถูกสร้างอัตโนมัติ ห้ามแก้ด้วยมือ ***
   สร้างจาก: python tools/generate_org_seed.py  (ต้นทาง tools/org_data.py)

   Purpose : ทะเบียนแผนก 10 แผนก และพนักงาน 100 คน (แผนกละ 10 คน)
   Idempotent : YES (MERGE ทั้งหมด รันซ้ำได้)
   ต้องรันหลัง 30_employee_kpi_model.sql

   แผนกชุดเดิม (LINE_A/B/C, QC, MAINT) ถูกปิดด้วย IsActive = 0 ไม่ลบทิ้ง
   เพราะมี foreign key จากข้อมูลเก่าชี้อยู่ และการปิดทำให้ย้อนกลับได้
   ด้วย UPDATE บรรทัดเดียว
   ============================================================= */

USE KpiMonthlyReport;
GO

IF OBJECT_ID('core.DimEmployee') IS NULL
BEGIN
    RAISERROR(N'หยุด: ไม่พบ core.DimEmployee (ต้องรัน 30_employee_kpi_model.sql ก่อน)', 16, 1);
    SET NOEXEC ON;
END
GO

/* ---------- 1) ปิดแผนกชุดเดิม ---------- */
UPDATE core.DimDepartment
SET IsActive = 0
WHERE DepartmentCode IN ('LINE_A','LINE_B','LINE_C','QC','MAINT');
GO

/* พนักงานเดิมที่ผูกกับแผนกที่ปิดไปแล้ว ต้องปิดตาม
   ไม่งั้นหน้า Monitoring จะมีคนไม่มีแผนกโผล่ขึ้นมา */
UPDATE e
SET e.IsActive = 0
FROM core.DimEmployee e
JOIN core.DimDepartment d ON d.DepartmentId = e.DepartmentId
WHERE d.IsActive = 0 AND e.EmployeeId > 0;
GO

/* ---------- 2) แผนก 10 แผนก ---------- */
MERGE core.DimDepartment AS t
USING (VALUES
    ('HR', N'Human Resources', N'ทรัพยากรบุคคล', 'PLANT1', N'สุนีย์ วัฒนกิจ'),
    ('FIN', N'Finance & Accounting', N'บัญชีและการเงิน', 'PLANT1', N'ธนกฤต ศรีสมบัติ'),
    ('IT', N'Information Technology', N'เทคโนโลยีสารสนเทศ', 'PLANT1', N'ปิยะพงษ์ อินทรชัย'),
    ('PROD1', N'Production Line 1', N'ฝ่ายผลิต 1', 'PLANT1', N'สมชาย ก้องเกียรติ'),
    ('PROD2', N'Production Line 2', N'ฝ่ายผลิต 2', 'PLANT1', N'สมหญิง พูนทรัพย์'),
    ('QA', N'Quality Assurance', N'ประกันคุณภาพ', 'PLANT1', N'วิภา เจริญสุข'),
    ('ENG', N'Engineering', N'วิศวกรรม', 'PLANT1', N'อนันต์ ไพศาล'),
    ('WH', N'Warehouse & Logistics', N'คลังสินค้าและขนส่ง', 'PLANT1', N'ประยุทธ มั่นคง'),
    ('PUR', N'Purchasing', N'จัดซื้อ', 'PLANT1', N'กมลชนก ทองดี'),
    ('SALE', N'Sales & Marketing', N'ขายและการตลาด', 'PLANT1', N'ณัฐวุฒิ บุญมา')
) AS s (DepartmentCode, DepartmentName, DepartmentNameTh, PlantCode, ManagerName)
ON t.DepartmentCode = s.DepartmentCode
WHEN MATCHED THEN UPDATE SET
    t.DepartmentName = s.DepartmentName, t.DepartmentNameTh = s.DepartmentNameTh,
    t.PlantCode = s.PlantCode, t.ManagerName = s.ManagerName, t.IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (DepartmentCode, DepartmentName, DepartmentNameTh, PlantCode, ManagerName)
    VALUES (s.DepartmentCode, s.DepartmentName, s.DepartmentNameTh, s.PlantCode, s.ManagerName);
GO

/* ---------- 3) Alias ชื่อแผนก (รองรับชื่อที่ต้นทางสะกดไม่ตรง) ----------
   เก็บแบบ normalize แล้วเพื่อให้ core.fn_NormalizeText จับคู่ได้ตรง ๆ */
;WITH src AS (
    SELECT core.fn_NormalizeText(v.AliasText) AS AliasText, d.DepartmentId
    FROM (VALUES
        (N'human resources', 'HR'),
        (N'hr', 'HR'),
        (N'ทรัพยากรบุคคล', 'HR'),
        (N'finance & accounting', 'FIN'),
        (N'fin', 'FIN'),
        (N'บัญชีและการเงิน', 'FIN'),
        (N'it', 'IT'),
        (N'เทคโนโลยีสารสนเทศ', 'IT'),
        (N'information technology', 'IT'),
        (N'ฝ่ายผลิต 1', 'PROD1'),
        (N'prod1', 'PROD1'),
        (N'production line 1', 'PROD1'),
        (N'production line 2', 'PROD2'),
        (N'prod2', 'PROD2'),
        (N'ฝ่ายผลิต 2', 'PROD2'),
        (N'qa', 'QA'),
        (N'quality assurance', 'QA'),
        (N'ประกันคุณภาพ', 'QA'),
        (N'วิศวกรรม', 'ENG'),
        (N'engineering', 'ENG'),
        (N'eng', 'ENG'),
        (N'คลังสินค้าและขนส่ง', 'WH'),
        (N'wh', 'WH'),
        (N'warehouse & logistics', 'WH'),
        (N'pur', 'PUR'),
        (N'จัดซื้อ', 'PUR'),
        (N'purchasing', 'PUR'),
        (N'sale', 'SALE'),
        (N'sales & marketing', 'SALE'),
        (N'ขายและการตลาด', 'SALE')
    ) AS v (AliasText, DeptCode)
    JOIN core.DimDepartment d ON d.DepartmentCode = v.DeptCode
)
MERGE core.DepartmentAlias AS t
USING (SELECT AliasText, MIN(DepartmentId) AS DepartmentId FROM src
       WHERE AliasText <> N'' GROUP BY AliasText) AS s
ON t.AliasText = s.AliasText
WHEN NOT MATCHED THEN INSERT (AliasText, DepartmentId) VALUES (s.AliasText, s.DepartmentId);
GO

/* ---------- 4) พนักงาน 100 คน ---------- */
MERGE core.DimEmployee AS t
USING (
    SELECT v.EmployeeCode, v.EmployeeName, d.DepartmentId, v.Position, v.HireDate
    FROM (VALUES
    ('EMP-0001', N'ศุภชัย ยอดเยี่ยม', 'HR', N'หัวหน้าแผนก', '2023-02-18'),
    ('EMP-0002', N'ภาณุพงศ์ ทองสุข', 'HR', N'ผู้ช่วยหัวหน้าแผนก', '2012-03-11'),
    ('EMP-0003', N'ศุภชัย หอมหวล', 'HR', N'เจ้าหน้าที่อาวุโส', '2018-11-05'),
    ('EMP-0004', N'สิริพร ศิริพันธ์', 'HR', N'เจ้าหน้าที่อาวุโส', '2012-10-02'),
    ('EMP-0005', N'ธีรศักดิ์ แสงทอง', 'HR', N'เจ้าหน้าที่', '2021-12-08'),
    ('EMP-0006', N'วรรณิศา เกษมสุข', 'HR', N'เจ้าหน้าที่', '2012-10-11'),
    ('EMP-0007', N'ปวีณา วงศ์สว่าง', 'HR', N'เจ้าหน้าที่', '2020-07-09'),
    ('EMP-0008', N'ปวีณา ศิริพันธ์', 'HR', N'พนักงานปฏิบัติการ', '2018-02-25'),
    ('EMP-0009', N'ยุทธนา แสงทอง', 'HR', N'พนักงานปฏิบัติการ', '2024-08-28'),
    ('EMP-0010', N'ปวีณา ทองสุข', 'HR', N'พนักงานปฏิบัติการ', '2023-02-02'),
    ('EMP-0011', N'อรุณี บุญเรือง', 'FIN', N'หัวหน้าแผนก', '2012-04-06'),
    ('EMP-0012', N'ชนาธิป อ่อนละมุน', 'FIN', N'ผู้ช่วยหัวหน้าแผนก', '2012-07-07'),
    ('EMP-0013', N'วรรณิศา ลิ้มเจริญ', 'FIN', N'เจ้าหน้าที่อาวุโส', '2012-02-20'),
    ('EMP-0014', N'รัตนาภรณ์ ศิริพันธ์', 'FIN', N'เจ้าหน้าที่อาวุโส', '2014-07-02'),
    ('EMP-0015', N'อดิศักดิ์ ศิริพันธ์', 'FIN', N'เจ้าหน้าที่', '2022-08-25'),
    ('EMP-0016', N'ยุทธนา ทองสุข', 'FIN', N'เจ้าหน้าที่', '2017-08-05'),
    ('EMP-0017', N'เกรียงไกร สมบูรณ์ทรัพย์', 'FIN', N'เจ้าหน้าที่', '2020-06-26'),
    ('EMP-0018', N'สิริพร ทองสุข', 'FIN', N'พนักงานปฏิบัติการ', '2022-10-09'),
    ('EMP-0019', N'ธีรศักดิ์ วงศ์สว่าง', 'FIN', N'พนักงานปฏิบัติการ', '2024-08-17'),
    ('EMP-0020', N'จิราพร ยอดเยี่ยม', 'FIN', N'พนักงานปฏิบัติการ', '2014-01-19'),
    ('EMP-0021', N'รัตนาภรณ์ อ่อนละมุน', 'IT', N'หัวหน้าแผนก', '2015-12-07'),
    ('EMP-0022', N'กิตติ พงษ์ไพบูลย์', 'IT', N'ผู้ช่วยหัวหน้าแผนก', '2014-10-16'),
    ('EMP-0023', N'กิตติ มหาวงศ์', 'IT', N'เจ้าหน้าที่อาวุโส', '2014-10-12'),
    ('EMP-0024', N'ปวีณา บุญเรือง', 'IT', N'เจ้าหน้าที่อาวุโส', '2019-09-18'),
    ('EMP-0025', N'เกรียงไกร ศิริพันธ์', 'IT', N'เจ้าหน้าที่', '2013-07-11'),
    ('EMP-0026', N'ณัฐพล อ่อนละมุน', 'IT', N'เจ้าหน้าที่', '2021-05-11'),
    ('EMP-0027', N'ธีรศักดิ์ พงษ์ไพบูลย์', 'IT', N'เจ้าหน้าที่', '2019-10-24'),
    ('EMP-0028', N'ธีรศักดิ์ แซ่ตั้ง', 'IT', N'พนักงานปฏิบัติการ', '2024-08-15'),
    ('EMP-0029', N'อรุณี ศิริพันธ์', 'IT', N'พนักงานปฏิบัติการ', '2019-03-17'),
    ('EMP-0030', N'ยุทธนา วงศ์สว่าง', 'IT', N'พนักงานปฏิบัติการ', '2018-08-28'),
    ('EMP-0031', N'พิมพ์ชนก แสงทอง', 'PROD1', N'หัวหน้าแผนก', '2013-04-15'),
    ('EMP-0032', N'สิริพร พงษ์ไพบูลย์', 'PROD1', N'ผู้ช่วยหัวหน้าแผนก', '2014-06-08'),
    ('EMP-0033', N'อดิศักดิ์ หอมหวล', 'PROD1', N'เจ้าหน้าที่อาวุโส', '2018-04-23'),
    ('EMP-0034', N'โสภณ สมบูรณ์ทรัพย์', 'PROD1', N'เจ้าหน้าที่อาวุโส', '2017-07-26'),
    ('EMP-0035', N'ธีรศักดิ์ สมบูรณ์ทรัพย์', 'PROD1', N'เจ้าหน้าที่', '2021-12-04'),
    ('EMP-0036', N'ศุภชัย แซ่ตั้ง', 'PROD1', N'เจ้าหน้าที่', '2020-11-28'),
    ('EMP-0037', N'จิราพร พงษ์ไพบูลย์', 'PROD1', N'เจ้าหน้าที่', '2014-02-27'),
    ('EMP-0038', N'กิตติ ลิ้มเจริญ', 'PROD1', N'พนักงานปฏิบัติการ', '2021-11-08'),
    ('EMP-0039', N'จิราพร เกษมสุข', 'PROD1', N'พนักงานปฏิบัติการ', '2020-09-14'),
    ('EMP-0040', N'เกรียงไกร อ่อนละมุน', 'PROD1', N'พนักงานปฏิบัติการ', '2017-03-27'),
    ('EMP-0041', N'แพรวพรรณ หอมหวล', 'PROD2', N'หัวหน้าแผนก', '2014-10-01'),
    ('EMP-0042', N'ปวีณา ลิ้มเจริญ', 'PROD2', N'ผู้ช่วยหัวหน้าแผนก', '2012-11-27'),
    ('EMP-0043', N'อดิศักดิ์ วงศ์สว่าง', 'PROD2', N'เจ้าหน้าที่อาวุโส', '2019-03-08'),
    ('EMP-0044', N'ปวีณา แสงทอง', 'PROD2', N'เจ้าหน้าที่อาวุโส', '2020-12-23'),
    ('EMP-0045', N'บุญฤทธิ์ รุ่งเรือง', 'PROD2', N'เจ้าหน้าที่', '2012-12-28'),
    ('EMP-0046', N'ดวงใจ หอมหวล', 'PROD2', N'เจ้าหน้าที่', '2012-02-06'),
    ('EMP-0047', N'มณีรัตน์ หอมหวล', 'PROD2', N'เจ้าหน้าที่', '2016-09-23'),
    ('EMP-0048', N'พิมพ์ชนก ทองสุข', 'PROD2', N'พนักงานปฏิบัติการ', '2017-04-16'),
    ('EMP-0049', N'อรุณี แซ่ตั้ง', 'PROD2', N'พนักงานปฏิบัติการ', '2023-09-26'),
    ('EMP-0050', N'ไพโรจน์ วงศ์สว่าง', 'PROD2', N'พนักงานปฏิบัติการ', '2021-06-16')
    ) AS v (EmployeeCode, EmployeeName, DeptCode, Position, HireDate)
    JOIN core.DimDepartment d ON d.DepartmentCode = v.DeptCode
) AS s
ON t.EmployeeCode = s.EmployeeCode
WHEN MATCHED THEN UPDATE SET
    t.EmployeeName = s.EmployeeName, t.DepartmentId = s.DepartmentId,
    t.Position = s.Position, t.HireDate = s.HireDate, t.IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (EmployeeCode, EmployeeName, DepartmentId, Position, HireDate)
    VALUES (s.EmployeeCode, s.EmployeeName, s.DepartmentId, s.Position, s.HireDate);
GO
MERGE core.DimEmployee AS t
USING (
    SELECT v.EmployeeCode, v.EmployeeName, d.DepartmentId, v.Position, v.HireDate
    FROM (VALUES
    ('EMP-0051', N'ภาณุพงศ์ บุญเรือง', 'QA', N'หัวหน้าแผนก', '2018-02-05'),
    ('EMP-0052', N'ภาณุพงศ์ มหาวงศ์', 'QA', N'ผู้ช่วยหัวหน้าแผนก', '2016-07-17'),
    ('EMP-0053', N'จิราพร หอมหวล', 'QA', N'เจ้าหน้าที่อาวุโส', '2021-03-26'),
    ('EMP-0054', N'พิมพ์ชนก อ่อนละมุน', 'QA', N'เจ้าหน้าที่อาวุโส', '2020-02-15'),
    ('EMP-0055', N'บุญฤทธิ์ บุญเรือง', 'QA', N'เจ้าหน้าที่', '2021-10-01'),
    ('EMP-0056', N'โสภณ ศิริพันธ์', 'QA', N'เจ้าหน้าที่', '2024-02-20'),
    ('EMP-0057', N'ธีรศักดิ์ เกษมสุข', 'QA', N'เจ้าหน้าที่', '2023-08-04'),
    ('EMP-0058', N'ยุทธนา พงษ์ไพบูลย์', 'QA', N'พนักงานปฏิบัติการ', '2023-02-11'),
    ('EMP-0059', N'ปวีณา ยอดเยี่ยม', 'QA', N'พนักงานปฏิบัติการ', '2016-05-01'),
    ('EMP-0060', N'บุญฤทธิ์ ลิ้มเจริญ', 'QA', N'พนักงานปฏิบัติการ', '2023-05-14'),
    ('EMP-0061', N'ลักษมี บุญเรือง', 'ENG', N'หัวหน้าแผนก', '2018-11-17'),
    ('EMP-0062', N'จิราพร สมบูรณ์ทรัพย์', 'ENG', N'ผู้ช่วยหัวหน้าแผนก', '2020-09-19'),
    ('EMP-0063', N'ภาณุพงศ์ อ่อนละมุน', 'ENG', N'เจ้าหน้าที่อาวุโส', '2012-09-16'),
    ('EMP-0064', N'ชนาธิป แสงทอง', 'ENG', N'เจ้าหน้าที่อาวุโส', '2017-11-21'),
    ('EMP-0065', N'เกรียงไกร หอมหวล', 'ENG', N'เจ้าหน้าที่', '2020-07-20'),
    ('EMP-0066', N'บุญฤทธิ์ วงศ์สว่าง', 'ENG', N'เจ้าหน้าที่', '2013-01-15'),
    ('EMP-0067', N'ไพโรจน์ รุ่งเรือง', 'ENG', N'เจ้าหน้าที่', '2020-02-07'),
    ('EMP-0068', N'ณัฐพล ลิ้มเจริญ', 'ENG', N'พนักงานปฏิบัติการ', '2023-03-04'),
    ('EMP-0069', N'ยุทธนา ลิ้มเจริญ', 'ENG', N'พนักงานปฏิบัติการ', '2017-03-07'),
    ('EMP-0070', N'อดิศักดิ์ ยอดเยี่ยม', 'ENG', N'พนักงานปฏิบัติการ', '2018-12-14'),
    ('EMP-0071', N'รัตนาภรณ์ ทองสุข', 'WH', N'หัวหน้าแผนก', '2019-12-28'),
    ('EMP-0072', N'พิมพ์ชนก แซ่ตั้ง', 'WH', N'ผู้ช่วยหัวหน้าแผนก', '2022-09-07'),
    ('EMP-0073', N'อดิศักดิ์ เกษมสุข', 'WH', N'เจ้าหน้าที่อาวุโส', '2022-11-12'),
    ('EMP-0074', N'สิริพร บุญเรือง', 'WH', N'เจ้าหน้าที่อาวุโส', '2020-09-06'),
    ('EMP-0075', N'ณัฐพล ศิริพันธ์', 'WH', N'เจ้าหน้าที่', '2022-08-19'),
    ('EMP-0076', N'ไพโรจน์ ศิริพันธ์', 'WH', N'เจ้าหน้าที่', '2016-05-15'),
    ('EMP-0077', N'อดิศักดิ์ พงษ์ไพบูลย์', 'WH', N'เจ้าหน้าที่', '2022-03-01'),
    ('EMP-0078', N'กฤษณะ หอมหวล', 'WH', N'พนักงานปฏิบัติการ', '2012-10-13'),
    ('EMP-0079', N'ปวีณา หอมหวล', 'WH', N'พนักงานปฏิบัติการ', '2019-10-04'),
    ('EMP-0080', N'แพรวพรรณ รุ่งเรือง', 'WH', N'พนักงานปฏิบัติการ', '2016-09-13'),
    ('EMP-0081', N'กฤษณะ ลิ้มเจริญ', 'PUR', N'หัวหน้าแผนก', '2013-08-15'),
    ('EMP-0082', N'โสภณ ลิ้มเจริญ', 'PUR', N'ผู้ช่วยหัวหน้าแผนก', '2017-07-21'),
    ('EMP-0083', N'รัตนาภรณ์ มหาวงศ์', 'PUR', N'เจ้าหน้าที่อาวุโส', '2018-12-20'),
    ('EMP-0084', N'กฤษณะ แซ่ตั้ง', 'PUR', N'เจ้าหน้าที่อาวุโส', '2021-09-24'),
    ('EMP-0085', N'ลักษมี ศิริพันธ์', 'PUR', N'เจ้าหน้าที่', '2013-06-03'),
    ('EMP-0086', N'กฤษณะ พงษ์ไพบูลย์', 'PUR', N'เจ้าหน้าที่', '2017-02-28'),
    ('EMP-0087', N'ปวีณา รุ่งเรือง', 'PUR', N'เจ้าหน้าที่', '2016-04-03'),
    ('EMP-0088', N'ดวงใจ ทองสุข', 'PUR', N'พนักงานปฏิบัติการ', '2014-08-13'),
    ('EMP-0089', N'ลักษมี เกษมสุข', 'PUR', N'พนักงานปฏิบัติการ', '2022-06-17'),
    ('EMP-0090', N'แพรวพรรณ แซ่ตั้ง', 'PUR', N'พนักงานปฏิบัติการ', '2014-03-25'),
    ('EMP-0091', N'มณีรัตน์ แสงทอง', 'SALE', N'หัวหน้าแผนก', '2024-10-19'),
    ('EMP-0092', N'บุญฤทธิ์ สมบูรณ์ทรัพย์', 'SALE', N'ผู้ช่วยหัวหน้าแผนก', '2017-02-08'),
    ('EMP-0093', N'อรุณี มหาวงศ์', 'SALE', N'เจ้าหน้าที่อาวุโส', '2014-02-03'),
    ('EMP-0094', N'นภัสสร หอมหวล', 'SALE', N'เจ้าหน้าที่อาวุโส', '2020-12-02'),
    ('EMP-0095', N'วรรณิศา ยอดเยี่ยม', 'SALE', N'เจ้าหน้าที่', '2023-04-25'),
    ('EMP-0096', N'มณีรัตน์ แซ่ตั้ง', 'SALE', N'เจ้าหน้าที่', '2017-05-12'),
    ('EMP-0097', N'กฤษณะ บุญเรือง', 'SALE', N'เจ้าหน้าที่', '2023-08-07'),
    ('EMP-0098', N'รัตนาภรณ์ บุญเรือง', 'SALE', N'พนักงานปฏิบัติการ', '2019-07-12'),
    ('EMP-0099', N'จิราพร มหาวงศ์', 'SALE', N'พนักงานปฏิบัติการ', '2023-09-19'),
    ('EMP-0100', N'ศุภชัย สมบูรณ์ทรัพย์', 'SALE', N'พนักงานปฏิบัติการ', '2017-09-21')
    ) AS v (EmployeeCode, EmployeeName, DeptCode, Position, HireDate)
    JOIN core.DimDepartment d ON d.DepartmentCode = v.DeptCode
) AS s
ON t.EmployeeCode = s.EmployeeCode
WHEN MATCHED THEN UPDATE SET
    t.EmployeeName = s.EmployeeName, t.DepartmentId = s.DepartmentId,
    t.Position = s.Position, t.HireDate = s.HireDate, t.IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (EmployeeCode, EmployeeName, DepartmentId, Position, HireDate)
    VALUES (s.EmployeeCode, s.EmployeeName, s.DepartmentId, s.Position, s.HireDate);
GO

/* ---------- 5) ตรวจผล ---------- */
SELECT 'DimDepartment (active)' AS TableName, COUNT(*) AS Rows
FROM core.DimDepartment WHERE IsActive = 1
UNION ALL SELECT 'DimEmployee (active)', COUNT(*)
FROM core.DimEmployee WHERE IsActive = 1 AND EmployeeId > 0
UNION ALL SELECT 'DepartmentAlias', COUNT(*) FROM core.DepartmentAlias;
GO

SELECT d.DepartmentCode, d.DepartmentNameTh, COUNT(e.EmployeeId) AS Headcount
FROM core.DimDepartment d
LEFT JOIN core.DimEmployee e ON e.DepartmentId = d.DepartmentId AND e.IsActive = 1
WHERE d.IsActive = 1
GROUP BY d.DepartmentCode, d.DepartmentNameTh
ORDER BY d.DepartmentCode;
GO

PRINT '>> 31_org_seed.sql เสร็จสมบูรณ์';
GO

/* ปลด NOEXEC เสมอ ไม่งั้นสคริปต์ถัดไปในหน้าต่าง SSMS เดิมจะเงียบไปทั้งไฟล์ */
SET NOEXEC OFF;
GO
