# KPI Monthly Report — Database Schema

สคริปต์สร้างฐานข้อมูลทั้งหมดของโปรเจกต์ ทุกไฟล์รันซ้ำได้ (Idempotent)

## ขอบเขตปัจจุบัน: ระบบนี้ "ไม่คำนวณ KPI เอง"

ฝ่าย HR มีระบบที่คำนวณ KPI อยู่แล้ว ระบบนี้จึงทำหน้าที่
**ดึงค่าที่คำนวณเสร็จแล้วมาเก็บ จัดรูป และส่งรายงาน** เท่านั้น
การคำนวณซ้ำมีแต่จะทำให้ตัวเลขสองระบบไม่ตรงกันโดยไม่มีใครตอบได้ว่าอันไหนถูก

หน่วยข้อมูลเป็น **KPI รายบุคคล** ระดับแผนกเป็นผลรวมที่สร้างใหม่ได้เสมอ
จึงไม่มีทางที่ตัวเลขสองระดับจะไม่ตรงกัน

```
ระบบ KPI ต้นทาง
      │ (feed รายบุคคล)
      ▼
stg.KpiEmployeeFeedRaw ──> core.FactKpiEmployeeMonthly ──┬──> rpt.vw_EmployeeKpiStatus     ──> หน้า Monitoring
                                                          │    rpt.vw_EmployeeKpiSummary
                                                          │    rpt.vw_DepartmentKpiCompletion
                                                          │
                                                          └──(rollup)──> core.FactKpiMonthly ──> rpt.* ──> Dashboard/Excel/PDF/อีเมล
```

ตอนนี้ยังต่อกับระบบต้นทางไม่ได้ ฝั่ง ETL จึงอ่านจากไฟล์ JSON จำลองใน
`mock-data/kpi-feed/` (`EmpKpiFeed_YYYYMM.json` สร้างจาก `tools/generate_employee_kpi_feed.py`) ผ่าน interface เดียวกับที่ของจริงจะใช้
(`IKpiFeedSource` → `MockJsonKpiFeedSource` / `HttpKpiFeedSource`)
วันเชื่อมต่อได้จริงเปลี่ยนแค่ค่า `KpiFeed:Provider` ใน App.config — ฐานข้อมูลไม่ต้องแก้

## ลำดับการรัน (ติดตั้งใหม่)

```
01_database_and_schemas.sql   สร้าง DB + schema stg/core/rpt/meta + database role
02_meta_tables.sql            KpiDefinition, KpiTarget, EtlRunLog, AuditLog ...
03_staging_tables.sql         ตารางรับข้อมูลดิบ
04_core_tables.sql            Dimension + Fact
05_utility_procs.sql          proc/function ที่ ETL และ Web เรียกใช้
06_rpt_views.sql              View + proc สำหรับ Dashboard
07_seed.sql                   ข้อมูลตั้งต้น + ปฏิทิน
08_parse_functions.sql        ฟังก์ชันแปลงข้อมูลสกปรก
14_rpt_department_view.sql    view รายชื่อแผนกสำหรับ dropdown
21_user_security.sql          ตารางฝั่ง Identity/RBAC
22_report_subscription.sql    ผู้รับรายงานอัตโนมัติ
23_report_subscription_user_link.sql
24_report_schedule.sql        วัน/เวลาส่งรายงานของแต่ละคน
26_kpi_feed_source.sql        ดึงค่า KPI จากต้นทาง (ระดับแผนก — ยังต้องรันเพราะ 30 ต่อยอดจากนี้)
30_employee_kpi_model.sql     *** สถาปัตยกรรมปัจจุบัน: KPI รายบุคคล + rollup ขึ้นระดับแผนก ***
                              (สร้าง core.DimEmployee / core.EmployeeAlias ให้เองด้วย ไม่ต้องไปรัน 15)
31_org_seed.sql               10 แผนก × 10 คน (สร้างจาก tools/generate_org_seed.py)
32_roles_and_report_scope.sql role เหลือ Admin/Manager + ผู้รับรายงานเลือกได้หลายแผนก
```

```bat
for %f in (01 02 03 04 05 06 07 08 14 21 22 23 24 26 30 31 32) do sqlcmd -S localhost -E -b -i %f*.sql
```

ต้องการ **SQL Server 2017 ขึ้นไป** (ใช้ `STRING_SPLIT` และ `STRING_AGG`)

`26` เป็นทั้ง migration ของฐานข้อมูลเดิมและตัว seed นิยาม KPI ของฐานข้อมูลใหม่
รันซ้ำได้ และมีด่านตรวจกันรันผิดฐานข้อมูลอยู่ต้นไฟล์

`27_verify_kpi_feed.sql` และ `33_verify_employee_kpi.sql` ไม่ใช่ส่วนหนึ่งของการติดตั้ง — เป็นชุดตรวจว่าระบบทำงานถูกต้อง
(อ่านอย่างเดียว) ทุกเช็คคืนคอลัมน์ `Result` = PASS / FAIL รันหลังโหลดข้อมูลรอบแรก

**ไฟล์ที่เลิกใช้แล้วถูกลบออกจากโฟลเดอร์นี้แล้ว** (2026-09-20)
`09` `10` `11` `11a` `12` `13` — ฝั่งการผลิต ถอดออกโดย `25`
`15` `16` `17` `18` `19` `20` — ฝั่งคำนวณ KPI จากข้อมูลลงเวลา ถอดออกโดย `26`
(ทะเบียนพนักงานที่ `15` เคยสร้าง ย้ายไปอยู่ใน `30` แล้ว โดยไม่เอาตารางลงเวลามาด้วย)
`25` — migration ถอด KPI การผลิต รันกับฐานปัจจุบันไปแล้ว
`SQLQuery1` `SQLQuery2` — สคริปต์ทดลองของ SSMS

ทั้งหมดยังอยู่ในประวัติ git กู้กลับมาได้ด้วย
`git checkout <commit> -- sql/<ชื่อไฟล์>` ถ้าต้องสร้างฐานข้อมูลรุ่นเก่าขึ้นมาใหม่

`27_verify_kpi_feed.sql` เป็นชุดตรวจของ feed ระดับแผนกซึ่งเลิกใช้แล้ว
แต่ยังไม่เคยถูก commit จึงเก็บไว้ก่อน ลบได้เมื่อยืนยันว่าไม่ต้องการ
ชุดตรวจของสถาปัตยกรรมปัจจุบันคือ `33_verify_employee_kpi.sql`

`CheckAccess.sql` เก็บไว้ — เป็น query มือสำหรับดู `ACCESS_DENIED` ใน `meta.AuditLog`

## แนวคิดการออกแบบ

**แบ่ง Schema 4 ชั้น** — `stg` → `core` → `rpt` โดยมี `meta` คุมนิยามและ log
เว็บอ่านได้เฉพาะ `rpt` เท่านั้น ทำให้เปลี่ยนโครงสร้าง `core` ได้โดยไม่พังหน้าจอ
การเปลี่ยนแหล่งข้อมูลครั้งนี้พิสูจน์ตัวมันเอง: ชั้น `rpt` ไม่ถูกแก้แม้แต่บรรทัดเดียว

**Staging เป็น NVARCHAR ทั้งหมด** — ค่าที่ต้นทางส่งมาเพี้ยน (เดือนผิดรูป,
ตัวเลขมี comma, รหัสแผนกไม่รู้จัก) จะไม่ทำให้การ import ล้มกลางคัน
ค่อยไปตรวจตอน Transform แถวที่ไม่ผ่านถูกบันทึกใน `meta.DataRejectLog` ไม่หายเงียบ ๆ

**Idempotency** — `core.FactKpiMonthly` มี UNIQUE บน (MonthKey, KpiId, DepartmentId)
และ `core.usp_Transform_KpiFeed` ลบเดือนนั้นก่อนโหลดใหม่ ดึงซ้ำข้อมูลจึงไม่บาน
ต้นทางแก้ค่าย้อนหลังแล้วส่งใหม่ ระบบจะยึดค่าล่าสุดเสมอ

**Unknown member** — `DepartmentId = -1` มีไว้รับข้อมูลที่หาคู่ไม่เจอ
ส่วน `DepartmentId = -99` คือระดับรวมทั้งบริษัท (feed ส่งมาเป็น `ALL`)

**DepartmentAlias** — จัดการปัญหาชื่อ/รหัสแผนกสะกดไม่ตรงกันด้วยตาราง map
ไม่ใช่ `CASE WHEN` ยาว ๆ ในโค้ด เพิ่ม alias ใหม่ได้โดยไม่ต้อง deploy

**การผูก KPI กับต้นทางอยู่ใน DB** — `meta.KpiDefinition.SourceMetricCode`
คือรหัสตัวชี้วัดฝั่งระบบต้นทาง เพิ่ม KPI ใหม่ = insert 1 แถว แล้วให้ต้นทางส่ง
รหัสนั้นมาใน feed ไม่ต้องแก้โค้ด C# และไม่ต้องเขียน stored procedure
(คอลัมน์ `CalcProcName` ของสถาปัตยกรรมเดิมยังอยู่แต่เป็น NULL และไม่มีใครเรียกใช้)

**สิ่งที่ระบบนี้ยังคำนวณเอง** — มีแค่ `PrevMonthValue` (ค่าเดือนก่อน) กับ
`StatusFlag` (สีเขียว/เหลือง/แดง) ใน `core.usp_RefreshKpi_Derived`
สองอย่างนี้ต้องมองข้ามเดือนและอิงทิศทาง H/L ของ KPI ซึ่งต้นทางที่ส่งมาทีละเดือน
ทำแทนให้ไม่ได้ และไม่ได้แตะค่า KPI เอง

**หารศูนย์** — ทุกจุดที่มีการหารใช้ `NULLIF(x, 0)` ครอบไว้แล้ว

## เดือนที่ถือว่าใช้ได้ (rpt.vw_ValidMonth)

เดือนที่ต้นทางส่งค่าระดับบริษัท (`DepartmentId = -99`) มาครบทุก KPI ที่ `IsActive = 1`
เท่านั้นจึงจะโผล่บนหน้าจอ เดือนที่ feed มาไม่ครบจะถูกกรองออกอัตโนมัติ
ป้องกัน "เดือนผี" ที่มีข้อมูลหลุดมาบางส่วนแล้วดูเหมือนตัวเลขตก

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

## ขั้นถัดไป

- ต่อ API จริงของระบบ KPI ต้นทาง (เขียน `HttpKpiFeedSource` ให้ครบ + ตกลง contract ของ endpoint)
- SQL Agent Job / Task Scheduler script

## ข้อควรระวังเรื่องเวอร์ชัน

- ใช้ `CREATE OR ALTER` ต้องการ **SQL Server 2016 SP1 ขึ้นไป**
- ใช้ `STRING_AGG` / `CHOOSE` / `FORMAT` ต้องการ **2012+**
- ถ้าใช้ **SQL Server Express** จะไม่มี SQL Server Agent
  ให้ใช้ Console App + Windows Task Scheduler แทน


## KPI รายบุคคล (ตั้งแต่ `30`)

โจทย์ที่ค่าระดับแผนกตอบไม่ได้: แผนกที่ค่าเฉลี่ยผ่านเกณฑ์ อาจมีคนที่ยังไม่ได้
เริ่มทำ KPI เลยซ่อนอยู่ข้างใน หน้า Monitoring จึงต้องดูลงไปถึงรายคน

**หน่วยข้อมูล** — `core.FactKpiEmployeeMonthly` หนึ่งแถว = พนักงาน 1 คน ×
KPI 1 ตัว × 1 เดือน พร้อมสถานะ `DONE` / `IN_PROGRESS` / `NOT_STARTED`
ระบบต้นทางส่งสถานะมาเองได้ ถ้าไม่ส่งระบบจะอนุมานจากค่าเทียบเป้าตามทิศทาง H/L
ซึ่งเป็นการแปลผลเพื่อนำเสนอ ไม่ใช่การคำนวณ KPI ใหม่

**นิยาม KPI** — `meta.KpiDefinition.Scope` แยก `EMPLOYEE` กับ `DEPARTMENT`
และ `IsChecklist = 1` สำหรับรายการที่วัดแค่ "ทำแล้ว/ยังไม่ทำ" (ไม่มีตัวเลข)
เพิ่ม KPI ตัวใหม่ = insert หนึ่งแถวที่ `30_employee_kpi_model.sql` แล้วให้ต้นทาง
ส่ง `SourceMetricCode` ตัวนั้นมา — ไม่ต้องแก้โค้ด C# และตารางบนหน้าจอขึ้นคอลัมน์ให้เอง

**การ rollup** — `core.usp_Rollup_KpiEmployeeToDept` สร้าง `core.FactKpiMonthly`
ขึ้นจากข้อมูลรายบุคคล: KPI แบบเช็กได้ % ของคนที่ทำเสร็จ, KPI มีตัวเลขได้ค่าเฉลี่ย
ของแผนก (ค่าเฉลี่ยไม่ใช่ผลรวม แผนกใหญ่จะได้ไม่ได้เปรียบเพียงเพราะมีคนเยอะ)
บวก KPI สังเคราะห์ `DEPT_KPI_COMPLETION` = ร้อยละ KPI ที่ทำสำเร็จทั้งแผนก
ETL เรียกให้อัตโนมัติทุกครั้งหลังโหลด feed สั่งเองได้ด้วย `KpiReport.Etl.exe rollup`

**สิทธิ์** — เหลือสอง role: `Admin` เห็นทุกแผนก, `Manager` เห็นเฉพาะแผนกที่ผูกไว้
ใน `meta.UserDepartment` (ผูกได้หลายแผนกต่อคน) การกรองทำที่ฝั่ง server ทุกครั้ง
ค่า DepartmentId ที่มาจาก URL หรือฟอร์มไม่เคยถูกเชื่อตรง ๆ

**ขอบเขตรายงาน** — `meta.ReportSubscriptionDepartment` ทำให้ผู้รับหนึ่งคน
เลือกได้หลายแผนกในรายงานฉบับเดียว (เดิมต้องสร้างหลายแถว = ได้อีเมลหลายฉบับ)
ไม่มีแถวลูก = ทุกแผนก

## เครื่องมือสร้างข้อมูลจำลอง

```
tools/org_data.py                    โครงสร้างองค์กร — แหล่งความจริงเดียวของทั้งสองสคริปต์
tools/generate_org_seed.py           -> sql/31_org_seed.sql
tools/generate_employee_kpi_feed.py  -> mock-data/kpi-feed/EmpKpiFeed_YYYYMM.json (6 เดือน)
```

แก้รายชื่อ/แผนกที่ `org_data.py` แล้วรันทั้งสองสคริปต์ใหม่ ทะเบียนกับ feed
จะอ้างคนชุดเดียวกันเสมอ ไม่งั้น feed จะยิงรหัสที่ไม่มีในทะเบียนแล้วตก reject ทั้งหมด
