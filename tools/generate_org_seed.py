# -*- coding: utf-8 -*-
"""
สร้าง sql/31_org_seed.sql จากโครงสร้างองค์กรใน org_data.py

    python tools/generate_org_seed.py

สร้างไฟล์ทับของเดิมได้เสมอ เพราะ SQL ที่ออกมาเป็น MERGE (idempotent)
แก้รายชื่อ/แผนกที่ org_data.py แล้วรันใหม่ ไม่ต้องแก้ SQL ด้วยมือ
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from org_data import DEPARTMENTS, employees  # noqa: E402

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
OUT = os.path.join(ROOT, "sql", "31_org_seed.sql")

HEADER = """/* =============================================================
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
"""

FOOTER = """
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
"""


def q(s):
    """escape single quote สำหรับ string literal ของ T-SQL"""
    return s.replace("'", "''")


def main():
    parts = [HEADER]

    # --- departments ---
    rows = ",\n".join(
        "    ('{0}', N'{1}', N'{2}', 'PLANT1', N'{3}')".format(
            q(code), q(en), q(th), q(mgr))
        for code, en, th, mgr in DEPARTMENTS
    )
    parts.append(
        "MERGE core.DimDepartment AS t\n"
        "USING (VALUES\n" + rows + "\n"
        ") AS s (DepartmentCode, DepartmentName, DepartmentNameTh, PlantCode, ManagerName)\n"
        "ON t.DepartmentCode = s.DepartmentCode\n"
        "WHEN MATCHED THEN UPDATE SET\n"
        "    t.DepartmentName = s.DepartmentName, t.DepartmentNameTh = s.DepartmentNameTh,\n"
        "    t.PlantCode = s.PlantCode, t.ManagerName = s.ManagerName, t.IsActive = 1\n"
        "WHEN NOT MATCHED THEN\n"
        "    INSERT (DepartmentCode, DepartmentName, DepartmentNameTh, PlantCode, ManagerName)\n"
        "    VALUES (s.DepartmentCode, s.DepartmentName, s.DepartmentNameTh, s.PlantCode, s.ManagerName);\n"
        "GO\n"
    )

    # --- aliases (จำลองชื่อแผนกที่ต้นทางสะกดไม่ตรง) ---
    alias_rows = []
    for code, en, th, _ in DEPARTMENTS:
        for alias in {code.lower(), code.replace("_", "-").lower(),
                      en.lower(), th}:
            alias_rows.append((alias, code))
    alias_sql = ",\n".join(
        "        (N'{0}', '{1}')".format(q(a), q(c)) for a, c in alias_rows
    )
    parts.append(
        "\n/* ---------- 3) Alias ชื่อแผนก (รองรับชื่อที่ต้นทางสะกดไม่ตรง) ----------\n"
        "   เก็บแบบ normalize แล้วเพื่อให้ core.fn_NormalizeText จับคู่ได้ตรง ๆ */\n"
        ";WITH src AS (\n"
        "    SELECT core.fn_NormalizeText(v.AliasText) AS AliasText, d.DepartmentId\n"
        "    FROM (VALUES\n" + alias_sql + "\n"
        "    ) AS v (AliasText, DeptCode)\n"
        "    JOIN core.DimDepartment d ON d.DepartmentCode = v.DeptCode\n"
        ")\n"
        "MERGE core.DepartmentAlias AS t\n"
        "USING (SELECT AliasText, MIN(DepartmentId) AS DepartmentId FROM src\n"
        "       WHERE AliasText <> N'' GROUP BY AliasText) AS s\n"
        "ON t.AliasText = s.AliasText\n"
        "WHEN NOT MATCHED THEN INSERT (AliasText, DepartmentId) VALUES (s.AliasText, s.DepartmentId);\n"
        "GO\n"
    )

    # --- employees (แบ่งเป็นก้อนละ 50 เพราะ VALUES ของ T-SQL จำกัด 1000 แถว
    #     และก้อนเล็กอ่าน error ตอนรันง่ายกว่า) ---
    emps = employees()
    parts.append("\n/* ---------- 4) พนักงาน {} คน ---------- */\n".format(len(emps)))
    for i in range(0, len(emps), 50):
        chunk = emps[i:i + 50]
        vals = ",\n".join(
            "    ('{0}', N'{1}', '{2}', N'{3}', '{4}')".format(
                q(e["code"]), q(e["name"]), q(e["dept"]),
                q(e["position_th"]), e["hire_date"])
            for e in chunk
        )
        parts.append(
            "MERGE core.DimEmployee AS t\n"
            "USING (\n"
            "    SELECT v.EmployeeCode, v.EmployeeName, d.DepartmentId, v.Position, v.HireDate\n"
            "    FROM (VALUES\n" + vals + "\n"
            "    ) AS v (EmployeeCode, EmployeeName, DeptCode, Position, HireDate)\n"
            "    JOIN core.DimDepartment d ON d.DepartmentCode = v.DeptCode\n"
            ") AS s\n"
            "ON t.EmployeeCode = s.EmployeeCode\n"
            "WHEN MATCHED THEN UPDATE SET\n"
            "    t.EmployeeName = s.EmployeeName, t.DepartmentId = s.DepartmentId,\n"
            "    t.Position = s.Position, t.HireDate = s.HireDate, t.IsActive = 1\n"
            "WHEN NOT MATCHED THEN\n"
            "    INSERT (EmployeeCode, EmployeeName, DepartmentId, Position, HireDate)\n"
            "    VALUES (s.EmployeeCode, s.EmployeeName, s.DepartmentId, s.Position, s.HireDate);\n"
            "GO\n"
        )

    parts.append(FOOTER)

    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("".join(parts))

    print("wrote {} ({} departments, {} employees)".format(
        os.path.relpath(OUT, ROOT), len(DEPARTMENTS), len(emps)))


if __name__ == "__main__":
    main()
