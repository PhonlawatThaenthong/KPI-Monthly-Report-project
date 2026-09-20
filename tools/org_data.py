# -*- coding: utf-8 -*-
"""
โครงสร้างองค์กรจำลอง — แหล่งความจริงเดียวของทั้ง seed SQL และ KPI feed

ทั้งสองฝั่งต้องเห็นรหัสแผนกและรหัสพนักงานชุดเดียวกัน ไม่งั้น feed จะยิง
รหัสที่ไม่มีในทะเบียน แล้วทุกแถวตกลง meta.DataRejectLog

    from org_data import DEPARTMENTS, employees, KPI_ITEMS
"""

import random

SEED = 2569

# (code, en, th, manager)
DEPARTMENTS = [
    ("HR",    "Human Resources",       "ทรัพยากรบุคคล",        "สุนีย์ วัฒนกิจ"),
    ("FIN",   "Finance & Accounting",  "บัญชีและการเงิน",       "ธนกฤต ศรีสมบัติ"),
    ("IT",    "Information Technology","เทคโนโลยีสารสนเทศ",     "ปิยะพงษ์ อินทรชัย"),
    ("PROD1", "Production Line 1",     "ฝ่ายผลิต 1",            "สมชาย ก้องเกียรติ"),
    ("PROD2", "Production Line 2",     "ฝ่ายผลิต 2",            "สมหญิง พูนทรัพย์"),
    ("QA",    "Quality Assurance",     "ประกันคุณภาพ",          "วิภา เจริญสุข"),
    ("ENG",   "Engineering",           "วิศวกรรม",              "อนันต์ ไพศาล"),
    ("WH",    "Warehouse & Logistics", "คลังสินค้าและขนส่ง",    "ประยุทธ มั่นคง"),
    ("PUR",   "Purchasing",            "จัดซื้อ",               "กมลชนก ทองดี"),
    ("SALE",  "Sales & Marketing",     "ขายและการตลาด",         "ณัฐวุฒิ บุญมา"),
]

# ตำแหน่งตามลำดับในแผนก (10 คน = หัวหน้า 1 + รองหัวหน้า 1 + ทีม 8)
POSITIONS = [
    ("Supervisor",        "หัวหน้าแผนก"),
    ("Assistant Manager", "ผู้ช่วยหัวหน้าแผนก"),
    ("Senior Staff",      "เจ้าหน้าที่อาวุโส"),
    ("Senior Staff",      "เจ้าหน้าที่อาวุโส"),
    ("Staff",             "เจ้าหน้าที่"),
    ("Staff",             "เจ้าหน้าที่"),
    ("Staff",             "เจ้าหน้าที่"),
    ("Operator",          "พนักงานปฏิบัติการ"),
    ("Operator",          "พนักงานปฏิบัติการ"),
    ("Operator",          "พนักงานปฏิบัติการ"),
]

FIRST_NAMES = [
    "กิตติ", "จิราพร", "ชนาธิป", "ณัฐพล", "ดวงใจ", "ธีรศักดิ์", "นภัสสร", "บุญฤทธิ์",
    "ปวีณา", "พิมพ์ชนก", "ภาณุพงศ์", "มณีรัตน์", "ยุทธนา", "รัตนาภรณ์", "ลักษมี",
    "วรรณิศา", "ศุภชัย", "สิริพร", "อดิศักดิ์", "อรุณี", "เกรียงไกร", "แพรวพรรณ",
    "โสภณ", "ไพโรจน์", "กฤษณะ",
]

LAST_NAMES = [
    "แซ่ตั้ง", "ทองสุข", "บุญเรือง", "พงษ์ไพบูลย์", "มหาวงศ์", "ยอดเยี่ยม", "รุ่งเรือง",
    "ลิ้มเจริญ", "วงศ์สว่าง", "ศิริพันธ์", "สมบูรณ์ทรัพย์", "หอมหวล", "อ่อนละมุน",
    "เกษมสุข", "แสงทอง",
]

# KPI รายบุคคล — ต้องตรงกับ meta.KpiDefinition ที่ seed ใน 27_employee_kpi_model.sql
# (code, checklist?, direction, target, decimals)
KPI_ITEMS = [
    ("EMP_ATTENDANCE", False, "H", 95.0, 2),
    ("EMP_OVERTIME",   False, "L", 20.0, 1),
    ("EMP_TRAINING",   False, "H", 6.0,  1),
    ("EMP_SAFETY",     True,  "H", 1.0,  0),
    ("EMP_KAIZEN",     False, "H", 1.0,  0),
    ("EMP_SELF_EVAL",  True,  "H", 1.0,  0),
]


def employees():
    """พนักงาน 100 คน — แผนกละ 10 คน รหัสเรียงต่อเนื่องทั้งบริษัท

    คืนค่า list ของ dict: code, name, dept, position_en, position_th, hire_date
    ใช้ seed คงที่ จึงได้ผลเหมือนเดิมทุกครั้งที่รัน — seed SQL กับ feed
    ที่สร้างคนละรอบจะอ้างคนคนเดียวกันเสมอ
    """
    rng = random.Random(SEED)
    rows = []
    seq = 0
    used = set()

    for dept_code, _, _, _ in DEPARTMENTS:
        for i in range(10):
            seq += 1
            # กันชื่อซ้ำทั้งบริษัท เพราะชื่อซ้ำทำให้ตรวจข้อมูลด้วยตาสับสน
            for _ in range(200):
                name = f"{rng.choice(FIRST_NAMES)} {rng.choice(LAST_NAMES)}"
                if name not in used:
                    break
            used.add(name)

            pos_en, pos_th = POSITIONS[i]
            year = rng.randint(2012, 2024)
            rows.append({
                "code": f"EMP-{seq:04d}",
                "name": name,
                "dept": dept_code,
                "position_en": pos_en,
                "position_th": pos_th,
                "hire_date": f"{year}-{rng.randint(1,12):02d}-{rng.randint(1,28):02d}",
            })
    return rows


if __name__ == "__main__":
    emps = employees()
    print(f"{len(DEPARTMENTS)} departments, {len(emps)} employees")
    for e in emps[:3]:
        print(e)
