# KPI Monthly Report — Database Schema

สคริปต์สร้างฐานข้อมูลทั้งหมดของโปรเจกต์ ทุกไฟล์รันซ้ำได้ (Idempotent)

## ลำดับการรัน

```
01_database_and_schemas.sql   สร้าง DB + schema stg/core/rpt/meta + database role
02_meta_tables.sql            KpiDefinition, KpiTarget, EtlRunLog, AuditLog ...
03_staging_tables.sql         ตารางรับข้อมูลดิบ 3 แหล่ง
04_core_tables.sql            Dimension + Fact
05_utility_procs.sql          proc/function ที่ ETL และ Web เรียกใช้
06_rpt_views.sql              View + proc สำหรับ Dashboard
07_seed.sql                   ข้อมูลตั้งต้น + ปฏิทิน + นิยาม KPI
```

รันใน SSMS ตามลำดับ หรือใช้ `sqlcmd`:

```bat
for %f in (01 02 03 04 05 06 07) do sqlcmd -S localhost -E -b -i %f*.sql
```

## แนวคิดการออกแบบ

**แบ่ง Schema 4 ชั้น** — `stg` → `core` → `rpt` โดยมี `meta` คุมนิยามและ log
เว็บอ่านได้เฉพาะ `rpt` เท่านั้น ทำให้เปลี่ยนโครงสร้าง `core` ได้โดยไม่พังหน้าจอ

**Staging เป็น NVARCHAR ทั้งหมด** — ข้อมูลสกปรก (วันที่ผิดรูป, ตัวเลขมี comma,
ค่าว่าง) จะไม่ทำให้การ import ล้มกลางคัน ค่อยไปตรวจตอน Transform
แถวที่ไม่ผ่านจะถูกบันทึกใน `meta.DataRejectLog` ไม่หายไปเฉย ๆ

**Idempotency** — ทุก Fact มี UNIQUE บน natural key รองรับ `MERGE`
หรือจะใช้ `core.usp_PurgeMonth` ลบเดือนนั้นก่อนโหลดใหม่ก็ได้
รันซ้ำรอบเดิมข้อมูลจะไม่บาน

**Unknown member** — `DepartmentId = -1` และ `ProductId = -1` มีไว้รับข้อมูลที่หา
คู่ไม่เจอ ดีกว่าทิ้งแถวนั้นไป เพราะยอดรวมจะยังตรง และเห็นได้ว่ามีข้อมูลกำพร้าเท่าไร
ส่วน `DepartmentId = -99` คือระดับรวมทั้งบริษัท

**DepartmentAlias** — จัดการปัญหาชื่อแผนกสะกดไม่ตรงกันด้วยตาราง map
ไม่ใช่ `CASE WHEN` ยาว ๆ ในโค้ด เพิ่ม alias ใหม่ได้โดยไม่ต้อง deploy

**สูตร KPI อยู่ใน DB** — `meta.KpiDefinition.CalcProcName` ชี้ไปที่ชื่อ proc
เพิ่ม KPI ใหม่ = เขียน proc + insert 1 แถว ไม่ต้องแก้โค้ด C#

> ⚠️ ตั้งใจเก็บ **ชื่อ proc** ไม่ใช่ SQL expression
> ถ้าเก็บ expression แล้วเอาไปต่อสตริงรันด้วย dynamic SQL จะเปิดช่อง
> SQL Injection ทันที ถ้าจะทำจริงต้อง whitelist ชื่อ proc ก่อนเรียกเสมอ

**หารศูนย์** — ทุกจุดที่มีการหารใช้ `NULLIF(x, 0)` ครอบไว้แล้ว

## การเชื่อมกับ ASP.NET Identity

Identity จะสร้างตาราง `AspNetUsers` / `AspNetRoles` เองด้วย EF Migration
สคริปต์ชุดนี้เตรียม `meta.UserDepartment` ไว้ผูก `UserId` (nvarchar(128)) กับแผนก
ไม่ได้ใส่ FK เพราะตาราง Identity ยังไม่มีตอนรันสคริปต์นี้

**Role ที่วางไว้**

| Role | สิทธิ์ |
|---|---|
| Admin | จัดการ user, แก้ KPI Definition/Target, ดู ETL log |
| Manager | ดูทุกแผนก, export รายงาน |
| Viewer | ดูเฉพาะแผนกตัวเองใน `meta.UserDepartment` |

Row-level filter ทำที่ `rpt.usp_GetKpiDashboard` ผ่านพารามิเตอร์ `@DepartmentId`
โดย Controller เป็นคนตัดสินว่าจะส่งค่าอะไรลงไปตาม role ของ user
**อย่าให้ฝั่ง client ส่ง DepartmentId มาเองแล้วเชื่อ** ต้องอ่านจาก claim ของ user เสมอ

## Connection String สำหรับแต่ละส่วน

สร้าง SQL Login แยก 2 ตัว แล้วใส่เข้า role ที่เตรียมไว้ อย่าใช้ `sa`:

```sql
CREATE LOGIN kpi_etl_user WITH PASSWORD = '<strong>';
CREATE USER  kpi_etl_user FOR LOGIN kpi_etl_user;
ALTER ROLE db_kpi_etl ADD MEMBER kpi_etl_user;

CREATE LOGIN kpi_web_user WITH PASSWORD = '<strong>';
CREATE USER  kpi_web_user FOR LOGIN kpi_web_user;
ALTER ROLE db_kpi_web ADD MEMBER kpi_web_user;
```

หมายเหตุ: user ของ ASP.NET Identity ต้องมีสิทธิ์เขียนตาราง `AspNetUsers`
ด้วย จึงอาจต้องเพิ่ม `db_datareader` / `db_datawriter` เฉพาะ schema `dbo`
ให้ `kpi_web_user` แยกต่างหาก

## สิ่งที่ยังไม่มีในชุดนี้ (ขั้นถัดไป)

- `core.usp_CalcKpi_*` ทั้ง 5 ตัว — proc คำนวณ KPI จริง
- Transform proc: `stg` → `core`
- Mock data generator
- SQL Agent Job / Task Scheduler script

## ข้อควรระวังเรื่องเวอร์ชัน

- ใช้ `CREATE OR ALTER` ต้องการ **SQL Server 2016 SP1 ขึ้นไป**
- ใช้ `STRING_AGG` / `CHOOSE` / `FORMAT` ต้องการ **2012+**
- ถ้าใช้ **SQL Server Express** จะไม่มี SQL Server Agent
  ให้ใช้ Console App + Windows Task Scheduler แทน
