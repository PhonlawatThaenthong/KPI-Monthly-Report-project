# -*- coding: utf-8 -*-
"""
สร้างข้อมูลจำลองของ "KPI feed ระดับบุคคล" — ค่าที่ระบบ HR ต้นทางคำนวณเสร็จแล้ว

ระบบนี้ไม่คำนวณ KPI เอง แต่ยังต่อกับระบบต้นทางไม่ได้ สคริปต์นี้จึงสร้าง
ไฟล์ JSON รายเดือนหน้าตาเหมือน response ของ REST API ที่ตกลงกันไว้
เพื่อให้ ETL (MockJsonKpiFeedSource) ทำงานได้ครบวงจรไปก่อน

    python tools/generate_employee_kpi_feed.py

ผลลัพธ์: mock-data/kpi-feed/EmpKpiFeed_YYYYMM.json  (6 เดือนล่าสุด)
         10 แผนก × 10 คน × 6 KPI = 600 แถวต่อเดือน

สิ่งที่จงใจใส่ไว้เพื่อให้ทดสอบระบบได้จริง
  - คนที่ทำ KPI ครบและไม่ครบคละกันในทุกแผนก (โจทย์หลักของหน้า Monitoring)
  - เดือนล่าสุดยังทำไม่เสร็จ — สถานะ IN_PROGRESS เยอะกว่าเดือนที่ปิดแล้ว
  - ชื่อแผนกสะกดไม่ตรง (prod-1, sale) -> ต้องถูก DepartmentAlias จับคู่ได้
  - ตัวเลขส่งมาเป็น string มี comma -> ต้องถูก core.fn_ParseDecimal แปลงได้
  - "N/A" และ null                  -> ต้องเข้า meta.DataRejectLog พร้อมเหตุผล
  - รหัสตัวชี้วัด/รหัสพนักงานที่ไม่รู้จัก -> ต้องถูกตัดออก ไม่ใช่ทำทั้งรอบล้ม
"""

import json
import os
import random
import sys
from calendar import monthrange
from datetime import datetime

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from org_data import DEPARTMENTS, KPI_ITEMS, SEED, employees  # noqa: E402

OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                       "..", "mock-data", "kpi-feed")

# 6 เดือนล่าสุด — เดือนสุดท้ายคือเดือนที่ยังทำอยู่
LAST_YEAR, LAST_MONTH = 2026, 9
MONTH_COUNT = 6

# ชื่อแผนกที่ต้นทางสะกดไม่ตรง (ต้องมีใน core.DepartmentAlias)
DIRTY_DEPT = {"PROD1": "prod-1", "SALE": "sales & marketing", "WH": "warehouse & logistics"}


def months_back(year, month, count):
    out = []
    y, m = year, month
    for _ in range(count):
        out.append((y, m))
        m -= 1
        if m == 0:
            y, m = y - 1, 12
    return list(reversed(out))


def diligence(rng):
    """นิสัยการทำ KPI ของพนักงานหนึ่งคน — คงที่ตลอดทั้งปี

    0.95 = ทำครบเกือบทุกเดือน, 0.45 = ตกหล่นบ่อย
    ให้กระจายตัวจริง ๆ ไม่ใช่ดีทุกคนหรือแย่ทุกคน ไม่งั้นหน้า Monitoring
    จะไม่มีอะไรให้ดู
    """
    return rng.choice([0.95, 0.90, 0.85, 0.75, 0.70, 0.60, 0.55, 0.45])


def metric_value(rng, kpi_code, target, will_complete, is_current_month):
    """คืน (actual, status) ของ KPI หนึ่งตัวของคนหนึ่งในเดือนหนึ่ง"""
    if kpi_code == "EMP_ATTENDANCE":
        if will_complete:
            actual = round(rng.uniform(target, 100.0), 2)
        else:
            actual = round(rng.uniform(85.0, target - 0.2), 2)
        return actual, None

    if kpi_code == "EMP_OVERTIME":   # ยิ่งน้อยยิ่งดี เป้า = ไม่เกิน 20 ชม.
        if will_complete:
            actual = round(rng.uniform(0.0, target), 1)
        else:
            actual = round(rng.uniform(target + 0.5, target * 2.2), 1)
        return actual, None

    if kpi_code == "EMP_TRAINING":   # เป้า 6 ชม./เดือน
        if will_complete:
            actual = round(rng.uniform(target, target * 2.0), 1)
        elif is_current_month:
            actual = round(rng.uniform(0.0, target - 0.5), 1)   # กำลังสะสมอยู่
        else:
            actual = round(rng.choice([0.0, rng.uniform(0.5, target - 0.5)]), 1)
        return actual, None

    if kpi_code == "EMP_KAIZEN":     # เป้า 1 เรื่อง/เดือน
        return (float(rng.randint(1, 3)) if will_complete else 0.0), None

    # checklist: EMP_SAFETY / EMP_SELF_EVAL
    return (1.0 if will_complete else 0.0), None


def build_month(rng, emps, year, month, is_current_month):
    records = []
    last_day = monthrange(year, month)[1]

    for emp in emps:
        rate = emp["_diligence"]
        # เดือนที่ยังไม่ปิด ทุกคนทำได้น้อยกว่าปกติ เพราะยังไม่ถึงสิ้นเดือน
        effective = rate * 0.55 if is_current_month else rate

        # ตัดสินก่อนว่าเดือนนี้คนนี้ "ครบทุกตัว" หรือไม่ แล้วค่อยลงรายละเอียด
        # ถ้าสุ่มทีละ KPI อย่างเดียว โอกาสครบทั้ง 6 ตัวจะเหลือไม่กี่ % ทำให้
        # หน้า Monitoring มีแต่คนไม่ครบ ซึ่งไม่ตรงกับความจริงขององค์กร
        fully_complete = rng.random() < effective
        if fully_complete:
            done_flags = [True] * len(KPI_ITEMS)
        else:
            done_flags = [rng.random() < effective * 0.75 for _ in KPI_ITEMS]
            if all(done_flags):                       # ต้องพลาดอย่างน้อยหนึ่งตัว
                done_flags[rng.randrange(len(KPI_ITEMS))] = False

        for (kpi_code, is_checklist, direction, target, decimals), will_complete \
                in zip(KPI_ITEMS, done_flags):
            actual, status = metric_value(rng, kpi_code, target,
                                          will_complete, is_current_month)

            # ปล่อยให้ฝั่ง SQL อนุมานสถานะเองเป็นส่วนใหญ่ (ทดสอบ logic นั้น)
            # ส่ง status มาบ้างเพื่อทดสอบเส้นทางที่ต้นทางระบุมาเอง
            if rng.random() < 0.25:
                if will_complete:
                    status = "DONE"
                elif actual and actual > 0:
                    status = "IN_PROGRESS"
                else:
                    status = "NOT_STARTED"

            completed = None
            if will_complete:
                day = rng.randint(1, last_day if not is_current_month else 20)
                completed = "{:04d}-{:02d}-{:02d}".format(year, month, day)

            dept_text = emp["dept"]
            if emp["dept"] in DIRTY_DEPT and rng.random() < 0.15:
                dept_text = DIRTY_DEPT[emp["dept"]]

            actual_out = actual
            # ตัวเลขใหญ่ส่งมาเป็น string มี comma บ้าง (ของจริงเจอบ่อย)
            if actual is not None and actual >= 1000 and rng.random() < 0.3:
                actual_out = "{:,.1f}".format(actual)

            records.append({
                "employeeCode": emp["code"],
                "employeeName": emp["name"],
                "department": dept_text,
                "metricCode": kpi_code,
                "target": target,
                "actual": actual_out,
                "status": status,
                "completedDate": completed,
            })

    # ---- ข้อมูลสกปรกที่ต้องถูกปฏิเสธอย่างสุภาพ ----
    records.append({
        "employeeCode": "EMP-9999", "employeeName": "Ghost Employee",
        "department": "HR", "metricCode": "EMP_TRAINING",
        "target": 6.0, "actual": 3.0, "status": None, "completedDate": None,
    })  # UNKNOWN_EMPLOYEE
    records.append({
        "employeeCode": emps[0]["code"], "employeeName": emps[0]["name"],
        "department": emps[0]["dept"], "metricCode": "EMP_MYSTERY_KPI",
        "target": 1.0, "actual": 1.0, "status": None, "completedDate": None,
    })  # UNKNOWN_KPI
    records.append({
        "employeeCode": emps[1]["code"], "employeeName": emps[1]["name"],
        "department": emps[1]["dept"], "metricCode": "EMP_TRAINING",
        "target": 6.0, "actual": "N/A", "status": None, "completedDate": None,
    })  # actual แปลงไม่ได้ -> NOT_STARTED (ไม่ใช่ทำให้ทั้งรอบล้ม)

    rng.shuffle(records)
    return records


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    rng = random.Random(SEED)

    emps = employees()
    for e in emps:
        e["_diligence"] = diligence(rng)

    period_list = months_back(LAST_YEAR, LAST_MONTH, MONTH_COUNT)
    written = []

    for idx, (year, month) in enumerate(period_list):
        is_current = (idx == len(period_list) - 1)
        records = build_month(rng, emps, year, month, is_current)

        payload = {
            "source": "HRIS-KPI-Service (mock)",
            "level": "EMPLOYEE",
            "period": "{:04d}-{:02d}".format(year, month),
            "generatedAt": datetime(year if month < 12 else year + 1,
                                    month + 1 if month < 12 else 1,
                                    5, 2, 0, 0).isoformat() + "+07:00",
            "isClosed": not is_current,
            "recordCount": len(records),
            "records": records,
        }

        path = os.path.join(OUT_DIR, "EmpKpiFeed_{:04d}{:02d}.json".format(year, month))
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(payload, f, ensure_ascii=False, indent=2)
        written.append((os.path.basename(path), len(records)))

    print("แผนก {} · พนักงาน {} คน · KPI {} ตัว".format(
        len(DEPARTMENTS), len(emps), len(KPI_ITEMS)))
    for name, n in written:
        print("  {}  {} records".format(name, n))


if __name__ == "__main__":
    main()
