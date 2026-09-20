# -*- coding: utf-8 -*-
"""
สร้างข้อมูลจำลองของ "KPI feed" — ค่าที่ระบบ KPI ต้นทางคำนวณเสร็จแล้ว

ระบบนี้ไม่คำนวณ KPI เอง แต่ตอนนี้ยังต่อกับระบบต้นทางไม่ได้
สคริปต์นี้จึงสร้างไฟล์ JSON รายเดือนที่หน้าตาเหมือน response ของ REST API
ที่ตกลงกันไว้ เพื่อให้ ETL (MockJsonKpiFeedSource) ทำงานได้ครบวงจรไปก่อน

    python tools/generate_kpi_feed.py

ผลลัพธ์: mock-data/kpi-feed/KpiFeed_YYYYMM.json  (2025-01 .. 2026-06)

สิ่งที่จงใจใส่ไว้เพื่อให้ทดสอบระบบได้จริง
  - แผนกสะกดไม่ตรง (L-A, line b)      -> ต้องถูก DepartmentAlias จับคู่ได้
  - ตัวเลขส่งมาเป็น string (มี comma ได้) -> ต้องถูก core.fn_ParseDecimal แปลงได้
  - "N/A" และ null                    -> ต้องเข้า meta.DataRejectLog พร้อมเหตุผล
  - รหัสตัวชี้วัดที่ไม่รู้จัก           -> ต้องถูกตัดออก ไม่ใช่ทำทั้งรอบล้ม
  - 2025-01 ส่งมาไม่ครบ KPI           -> ต้องถูก rpt.vw_ValidMonth กรองออก (เดือนผี)
"""

import json
import os
import random
from datetime import datetime

SEED = 2569
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "mock-data", "kpi-feed")

# ต้องตรงกับ core.DimDepartment ที่ seed ไว้ใน 07_seed.sql
DEPARTMENTS = ["LINE_A", "LINE_B", "LINE_C", "QC", "MAINT"]
HEADCOUNT = {"LINE_A": 9, "LINE_B": 9, "LINE_C": 8, "QC": 7, "MAINT": 7}

# ต้องตรงกับ meta.KpiDefinition.SourceMetricCode ที่ seed ไว้ใน 26_kpi_feed_source.sql
TARGETS = {"ATTENDANCE_RATE": 95.0, "OVERTIME_HRS": 200.0, "ABSENCE_RATE": 5.0}

# จำนวนวันทำงานต่อเดือน (โดยประมาณ ไม่นับวันหยุด)
WORKING_DAYS = {1: 21, 2: 19, 3: 21, 4: 19, 5: 21, 6: 21,
                7: 22, 8: 21, 9: 21, 10: 22, 11: 20, 12: 21}


def months(start_year, start_month, count):
    y, m = start_year, start_month
    for _ in range(count):
        yield y, m
        m += 1
        if m > 12:
            y, m = y + 1, 1


def department_metrics(rng, year, month, dept):
    """ค่า KPI ของแผนกหนึ่งในเดือนหนึ่ง — ให้ตัวตั้ง/ตัวหารสอดคล้องกับอัตราเสมอ"""
    working_days = WORKING_DAYS[month] * HEADCOUNT[dept]

    # อัตราการมางานแกว่งรอบ 95% แผนกซ่อมบำรุงต่ำกว่าเพื่อนเล็กน้อย
    base = 95.8 if dept != "MAINT" else 93.4
    attendance_rate = round(rng.gauss(base, 1.6), 2)
    attendance_rate = max(88.0, min(99.5, attendance_rate))
    present_days = int(round(working_days * attendance_rate / 100.0))

    # ขาด/ลา = ส่วนที่เหลือจากวันทำงาน (มาสายยังนับว่ามา จึงไม่ใช่ 100 - attendance เป๊ะ)
    absence_days = working_days - present_days
    absence_rate = round(absence_days * 100.0 / working_days, 2)

    # OT สูงขึ้นช่วงปลายไตรมาส
    quarter_end = 1.25 if month in (3, 6, 9, 12) else 1.0
    ot_hours = round(rng.gauss(165, 35) * quarter_end, 1)
    ot_hours = max(40.0, ot_hours)

    return [
        {"metricCode": "ATTENDANCE_RATE", "department": dept,
         "actual": attendance_rate, "target": TARGETS["ATTENDANCE_RATE"],
         "numerator": present_days, "denominator": working_days},
        {"metricCode": "OVERTIME_HRS", "department": dept,
         "actual": ot_hours, "target": TARGETS["OVERTIME_HRS"],
         "numerator": ot_hours, "denominator": None},
        {"metricCode": "ABSENCE_RATE", "department": dept,
         "actual": absence_rate, "target": TARGETS["ABSENCE_RATE"],
         "numerator": absence_days, "denominator": working_days},
    ]


def company_rows(metrics):
    """ระดับบริษัท (department = ALL) — รวมจากตัวตั้ง/ตัวหารจริง ไม่ใช่เฉลี่ยอัตรา
       เฉลี่ยอัตราตรง ๆ จะผิดเมื่อแต่ละแผนกมีคนไม่เท่ากัน"""
    rows = []
    for code in ("ATTENDANCE_RATE", "ABSENCE_RATE"):
        num = sum(m["numerator"] for m in metrics if m["metricCode"] == code)
        den = sum(m["denominator"] for m in metrics if m["metricCode"] == code)
        rows.append({"metricCode": code, "department": "ALL",
                     "actual": round(num * 100.0 / den, 2), "target": TARGETS[code],
                     "numerator": num, "denominator": den})

    ot = round(sum(m["actual"] for m in metrics if m["metricCode"] == "OVERTIME_HRS"), 1)
    rows.append({"metricCode": "OVERTIME_HRS", "department": "ALL",
                 "actual": ot, "target": TARGETS["OVERTIME_HRS"],
                 "numerator": ot, "denominator": None})
    return rows


def dirty_up(rng, rows, year, month):
    """ทำข้อมูลให้ "สกปรก" แบบที่ระบบต้นทางจริงมักส่งมา — เฉพาะบางเดือน
       ห้ามแตะแถว ALL เพราะ rpt.vw_ValidMonth ใช้ตัดสินว่าเดือนนั้นใช้ได้หรือไม่"""
    dept_rows = [r for r in rows if r["department"] != "ALL"]

    if month == 3:      # ชื่อแผนกสะกดไม่ตรง -> ทดสอบ DepartmentAlias
        for r in dept_rows:
            if r["department"] == "LINE_A":
                r["department"] = "L-A"
            elif r["department"] == "LINE_B":
                r["department"] = "line b"

    if month == 5:      # ตัวเลขส่งมาเป็น string (คั่นหลักพันด้วย comma) -> ทดสอบ fn_ParseDecimal
        for r in dept_rows:
            if r["metricCode"] == "OVERTIME_HRS":
                r["actual"] = "{:,.1f}".format(float(r["actual"]))

    if month == 8:      # ค่าหาย -> ต้องเข้า DataRejectLog ไม่ใช่เขียน 0 ลง fact
        victim = rng.choice([r for r in dept_rows if r["metricCode"] == "ABSENCE_RATE"])
        victim["actual"] = "N/A"

    if month == 11:     # ตัวชี้วัดที่ระบบนี้ไม่ได้ใช้ -> ต้องถูกตัดออกเงียบ ๆ พร้อมเหตุผล
        rows.append({"metricCode": "HEADCOUNT_TOTAL", "department": "QC",
                     "actual": 7, "target": None, "numerator": 7, "denominator": None})

    return rows


def build_month(rng, year, month):
    metrics = []
    for dept in DEPARTMENTS:
        metrics.extend(department_metrics(rng, year, month, dept))

    rows = metrics + company_rows(metrics)

    # 2025-01 : ต้นทางส่งมาไม่ครบ (มีแต่ ATTENDANCE_RATE) -> เดือนผี ต้องถูกกรองออก
    if (year, month) == (2025, 1):
        rows = [r for r in rows if r["metricCode"] == "ATTENDANCE_RATE"]
    else:
        rows = dirty_up(rng, rows, year, month)

    return {
        "source": "HRIS-KPI-Service (mock)",
        "generatedAt": datetime(year + (month // 12), (month % 12) + 1, 5, 2, 0, 0).isoformat() + "+07:00",
        "period": "{:04d}-{:02d}".format(year, month),
        "metrics": rows,
    }


def main():
    rng = random.Random(SEED)
    out_dir = os.path.normpath(OUT_DIR)
    if not os.path.isdir(out_dir):
        os.makedirs(out_dir)

    written = []
    for year, month in months(2025, 1, 18):          # 2025-01 .. 2026-06
        payload = build_month(rng, year, month)
        name = "KpiFeed_{:04d}{:02d}.json".format(year, month)
        path = os.path.join(out_dir, name)
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(payload, fh, ensure_ascii=False, indent=2)
            fh.write("\n")
        written.append((name, len(payload["metrics"])))

    print("สร้างไฟล์ทั้งหมด {} เดือน ที่ {}".format(len(written), out_dir))
    for name, count in written:
        print("  {}  {} ตัวชี้วัด".format(name, count))


if __name__ == "__main__":
    main()
