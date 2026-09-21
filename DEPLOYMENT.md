# คู่มือติดตั้งบนเครื่อง Server

ระบบรายงาน KPI รายเดือน (HR KPI Monitoring System) — ขั้นตอนติดตั้งตั้งแต่ฐานข้อมูล
จนถึงงานส่งอีเมลอัตโนมัติด้วย Windows Task Scheduler

เอกสารนี้เขียนสำหรับเครื่อง **Windows Server** ที่ยังไม่มีอะไรเลย ทำตามลำดับจากบนลงล่าง
ใช้เวลาประมาณ 1–2 ชั่วโมงรอบแรก

---

## 0. ระบบนี้ประกอบด้วยอะไร

| ส่วน | คืออะไร | รันที่ไหน |
|---|---|---|
| ฐานข้อมูล `KpiMonthlyReport` | ข้อมูล KPI, ผู้ใช้, ผู้รับรายงาน, log การส่ง | SQL Server |
| `KpiReport.Web` | เว็บ ASP.NET MVC — Dashboard / Monitoring / Users / Report emails | IIS |
| `KpiReport.Etl.exe` | console app — ดึงค่า KPI เข้าระบบ และ **ส่งอีเมลรายงาน** | Task Scheduler |

สิ่งที่ต้องเข้าใจก่อนเริ่ม: **เว็บไม่มีตัวจับเวลาอยู่ในตัว** วัน/เวลาส่งที่ตั้งในหน้า
Report emails เป็นแค่ข้อมูลในฐานข้อมูล คนที่ส่งจริงคือ `KpiReport.Etl.exe send-report`
ซึ่งต้องมี Task Scheduler มาเรียกให้ (ข้อ 6) ถ้าข้ามข้อนั้น รายงานจะไม่ถูกส่งเลย

เหตุที่ไม่ฝังตัวจับเวลาไว้ในเว็บ: IIS application pool มี idle timeout และ recycle ตัวเอง
งานตามตารางที่ฝากไว้ในเว็บจึงไม่รับประกันว่าจะได้รัน

---

## 1. สิ่งที่ต้องติดตั้งบนเครื่อง

- **Windows Server 2016 ขึ้นไป** (หรือ Windows 10/11 สำหรับเครื่องทดสอบ)
- **SQL Server 2017 ขึ้นไป** — สคริปต์ใช้ `STRING_AGG` / `STRING_SPLIT` ซึ่งมีตั้งแต่ 2017
  - SQL Express ใช้ได้ แต่ **ไม่มี SQL Server Agent** (มีผลกับข้อ 6 ทางเลือกที่ 2)
  - ติดตั้ง **SQL Server Management Studio (SSMS)** ไว้ด้วยเพื่อรันสคริปต์
- **.NET Framework 4.8** (Windows Server 2019 ขึ้นไปมีมาให้แล้ว)
- **IIS** พร้อม role features:
  - Web Server → Application Development → **ASP.NET 4.8**, .NET Extensibility 4.8, ISAPI Extensions, ISAPI Filters
  - Web Server → Security → **Windows Authentication** (ถ้าจะใช้), Request Filtering
  - Management Tools → IIS Management Console
- **Visual Studio 2019/2022** บนเครื่อง build (ไม่จำเป็นต้องมีบน server ถ้า build ที่อื่นแล้ว copy มา)

ตรวจว่า ASP.NET ลงทะเบียนกับ IIS แล้ว:

```bat
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\aspnet_regiis.exe -i
```

---

## 2. ติดตั้งฐานข้อมูล

### 2.1 รันสคริปต์ตามลำดับ

เปิด Command Prompt ในโฟลเดอร์ `sql\` แล้วรัน

```bat
for %f in (01 02 03 04 05 06 07 08 14 21 22 23 24 26 30 31 32 34) do sqlcmd -S localhost -E -b -i %f*.sql
```

เปลี่ยน `-S localhost` ตามชื่อ instance จริง (เช่น `-S localhost\SQLEXPRESS`)
ถ้าใช้ SQL authentication แทน Windows ให้ใช้ `-U sa -P <password>` แทน `-E`

**ลำดับสำคัญ** — `34` ต้องรันหลัง `32` เพราะ view ของ log อ้าง
`meta.vw_ReportSubscriptionAdmin` ที่สร้างใน `32`

สคริปต์ทุกตัว idempotent รันซ้ำได้ ไม่ทำข้อมูลหาย

รายละเอียดว่าแต่ละไฟล์ทำอะไร: `sql\README.md`

### 2.2 ตรวจว่าโครงสร้างครบ

```bat
sqlcmd -S localhost -E -d KpiMonthlyReport -i 33_verify_employee_kpi.sql
```

`33` เป็นชุดตรวจ ไม่ใช่ส่วนหนึ่งของการติดตั้ง — ควรผ่านทุกข้อก่อนไปต่อ

### 2.3 สร้าง login สำหรับเว็บและ ETL

โปรเจกต์ใช้ **Integrated Security** (Windows authentication) ทั้งสองฝั่ง
จึงไม่มีรหัสผ่านฐานข้อมูลอยู่ในไฟล์ config เลย ต้องสร้าง login ให้บัญชีที่รัน process

**ฝั่งเว็บ** — บัญชีคือ application pool identity ของข้อ 3 (`IIS APPPOOL\KpiReport`):

```sql
USE master;
CREATE LOGIN [IIS APPPOOL\KpiReport] FROM WINDOWS;
GO
USE KpiMonthlyReport;
CREATE USER [IIS APPPOOL\KpiReport] FOR LOGIN [IIS APPPOOL\KpiReport];

/* role ของโปรเจกต์ — ให้สิทธิ์เท่าที่หน้าเว็บต้องใช้ */
ALTER ROLE db_kpi_web ADD MEMBER [IIS APPPOOL\KpiReport];

/* ตาราง AspNetUsers/AspNetRoles ของ ASP.NET Identity อยู่ใน schema dbo
   ซึ่งไม่ได้อยู่ใน role ข้างบน จึงต้องให้สิทธิ์เพิ่ม */
ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\KpiReport];
ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\KpiReport];
GO
```

**ฝั่ง ETL** — บัญชีคือบัญชีที่ Task Scheduler ใช้รัน (ข้อ 6):

```sql
USE master;
CREATE LOGIN [DOMAIN\svc_kpi] FROM WINDOWS;
GO
USE KpiMonthlyReport;
CREATE USER [DOMAIN\svc_kpi] FOR LOGIN [DOMAIN\svc_kpi];
ALTER ROLE db_kpi_etl ADD MEMBER [DOMAIN\svc_kpi];

/* ETL เขียน log การส่งอีเมล และอ่านรายชื่อผู้รับ */
ALTER ROLE db_kpi_web ADD MEMBER [DOMAIN\svc_kpi];
GO
```

ถ้าเครื่องไม่ได้อยู่ใน domain ให้ใช้ `COMPUTERNAME\username` แทน `DOMAIN\svc_kpi`

---

## 3. Deploy เว็บขึ้น IIS

### 3.1 Build

ใน Visual Studio: เลือก configuration **Release** → คลิกขวาที่ `KpiReport.Web` →
**Publish** → Folder → ชี้ไปที่ `C:\inetpub\KpiReport`

หรือจาก command line:

```bat
msbuild src\KpiReport.Web\KpiReport.Web.csproj /p:Configuration=Release ^
  /p:DeployOnBuild=true /p:WebPublishMethod=FileSystem ^
  /p:publishUrl=C:\inetpub\KpiReport
```

### 3.2 สร้าง Application Pool

IIS Manager → Application Pools → Add Application Pool

- Name: `KpiReport`
- .NET CLR version: **.NET CLR v4.0**
- Managed pipeline mode: **Integrated**

แล้วเข้า Advanced Settings

- Identity: `ApplicationPoolIdentity` (ค่าเริ่มต้น — ตรงกับ login ในข้อ 2.3)
- **Idle Time-out (minutes): `0`** — ไม่ให้ pool หลับ ผู้ใช้เข้าหน้าแรกจะไม่ต้องรอ warm-up
- Regular Time Interval (minutes): `0` หรือกำหนดเวลา recycle นอกเวลางาน

### 3.3 สร้าง Site

IIS Manager → Sites → Add Website

- Site name: `KpiReport`
- Application pool: `KpiReport`
- Physical path: `C:\inetpub\KpiReport`
- Binding: http พอร์ต 80 (หรือ https พร้อม certificate — แนะนำ เพราะระบบมีหน้า login)

ให้สิทธิ์อ่านโฟลเดอร์กับ app pool:

```bat
icacls C:\inetpub\KpiReport /grant "IIS APPPOOL\KpiReport:(OI)(CI)(RX)" /T
```

### 3.4 แก้ Web.config

`C:\inetpub\KpiReport\Web.config`

```xml
<connectionStrings>
  <add name="KpiDb"
       connectionString="Data Source=localhost;Initial Catalog=KpiMonthlyReport;Integrated Security=True;TrustServerCertificate=True;"
       providerName="System.Data.SqlClient" />
</connectionStrings>
```

เปลี่ยน `Data Source` ถ้า SQL Server อยู่คนละเครื่องหรือคนละ instance

ตั้งค่า SMTP ในส่วน `appSettings` (ฝั่งเว็บใช้ตอน "ลืมรหัสผ่าน" และปุ่ม "ส่งเดี๋ยวนี้"):

```xml
<add key="Smtp.DeliveryMethod" value="Network" />
<add key="Smtp.Host"     value="smtp.office365.com" />
<add key="Smtp.Port"     value="587" />
<add key="Smtp.From"     value="kpi-report@company.com" />
<add key="Smtp.UserName" value="kpi-report@company.com" />
```

**รหัสผ่าน SMTP ไม่อยู่ในไฟล์นี้** — อ่านจาก environment variable `KPI_SMTP_PASSWORD`
สำหรับ IIS ต้องตั้งที่ระดับ application pool ไม่ใช่ `setx`:

IIS Manager → Application Pools → `KpiReport` → Advanced Settings →
**Environment Variables** → Add → Name `KPI_SMTP_PASSWORD` / Value `<รหัส>`
แล้ว recycle pool

(IIS เวอร์ชันเก่าที่ไม่มีช่องนี้ ให้ใช้ `setx /M KPI_SMTP_PASSWORD "<รหัส>"` แล้วรีสตาร์ท IIS
ข้อเสียคือทุก process บนเครื่องเห็นค่านี้)

### 3.5 เข้าระบบครั้งแรก

เปิดเว็บ → ระบบจะ seed บัญชี Admin ให้อัตโนมัติ (ดู `Infrastructure/IdentitySeeder.cs`)
**เปลี่ยนรหัสผ่านทันที** หลัง login ครั้งแรก

---

## 4. Deploy ETL

### 4.1 Copy ไฟล์

Build configuration **Release** แล้ว copy ทั้งโฟลเดอร์ `bin\Release` ไปที่

```
C:\Apps\KpiReport.Etl\
```

ต้องมีครบทั้ง `KpiReport.Etl.exe`, `KpiReport.Etl.exe.config`, และ DLL ทั้งหมด
(Dapper, ClosedXML, QuestPDF ฯลฯ) — ห้าม copy แค่ไฟล์ exe

### 4.2 แก้ `KpiReport.Etl.exe.config`

```xml
<connectionStrings>
  <add name="KpiDb"
       connectionString="Data Source=localhost;Initial Catalog=KpiMonthlyReport;Integrated Security=True;TrustServerCertificate=True;"
       providerName="System.Data.SqlClient" />
</connectionStrings>

<appSettings>
  <!-- แหล่งค่า KPI: mock = อ่านไฟล์ JSON / http = เรียก API ต้นทาง -->
  <add key="KpiFeed:Provider"   value="mock" />
  <add key="KpiFeed:MockFolder" value="C:\Apps\KpiReport.Etl\kpi-feed" />
  <add key="KpiFeed:BaseUrl"    value="" />

  <add key="Report:FromAddress" value="kpi-report@company.com" />
  <add key="Report:FromName"    value="HR KPI Monitoring System" />

  <add key="Smtp.DeliveryMethod" value="Network" />
  <add key="Smtp.Host"     value="smtp.office365.com" />
  <add key="Smtp.Port"     value="587" />
  <add key="Smtp.From"     value="kpi-report@company.com" />
  <add key="Smtp.UserName" value="kpi-report@company.com" />
</appSettings>
```

`KpiFeed:MockFolder` ต้องเป็น path บน server ไม่ใช่ path บนเครื่อง dev
ถ้ายังใช้โหมด mock ให้ copy โฟลเดอร์ `mock-data\kpi-feed` ไปวางไว้ที่นั้น

### 4.3 ตั้ง `KPI_SMTP_PASSWORD` ให้บัญชีที่รัน ETL

Environment variable ระดับผู้ใช้เป็นของแต่ละบัญชี ถ้า Task Scheduler รันด้วยบัญชี
`DOMAIN\svc_kpi` ต้องตั้งค่าในฐานะบัญชีนั้น — วิธีที่ตรงไปตรงมาที่สุดคือตั้งระดับเครื่อง:

```bat
setx /M KPI_SMTP_PASSWORD "<app password หรือรหัส SMTP>"
```

`/M` = machine level (ต้องรันจาก Command Prompt แบบ **Run as administrator**)
ทุก process บนเครื่องจะเห็นค่านี้ ยอมรับได้บน server ที่มีแต่ระบบนี้
ถ้าไม่ยอมรับ ให้ใช้ `runas /user:DOMAIN\svc_kpi cmd` แล้ว `setx` (ไม่ใส่ `/M`) ในนั้น

**ต้องเปิด session ใหม่หลัง setx** process ที่เปิดอยู่แล้วจะยังไม่เห็นค่าใหม่

### 4.4 ทดสอบด้วยมือก่อนตั้ง task

```bat
cd C:\Apps\KpiReport.Etl
KpiReport.Etl.exe run-all
KpiReport.Etl.exe rollup
KpiReport.Etl.exe send-report --dry-run
```

- `run-all` — ดึงค่า KPI จากต้นทางเข้าระบบ
- `rollup` — สรุป KPI รายบุคคลขึ้นเป็นระดับแผนก (ETL เรียกให้เองอยู่แล้ว สั่งซ้ำได้)
- `send-report --dry-run` — บอกว่าจะส่งอะไรให้ใคร **โดยไม่ส่งจริงและไม่เขียน log**

ถ้า `--dry-run` แสดงผลถูกต้อง ค่อยไปตั้ง Task Scheduler

---

## 5. เตรียมสคริปต์สำหรับ Task Scheduler

ในโฟลเดอร์ `tools\` มีให้แล้วสองไฟล์

| ไฟล์ | หน้าที่ |
|---|---|
| `send-report.cmd` | wrapper ที่ Task Scheduler เรียก — `cd` เข้าโฟลเดอร์ exe, เก็บ log, คืน exit code |
| `register-send-report-task.cmd` | ลงทะเบียน task ให้อัตโนมัติ (เหมาะกับเครื่อง dev) |

**บน server ต้องแก้ `send-report.cmd` ก่อน** เพราะค่าเริ่มต้นชี้ไปที่ `bin\Debug` ของเครื่อง dev

```bat
set "EXE_DIR=C:\Apps\KpiReport.Etl"
set "LOG_DIR=C:\Apps\KpiReport.Etl\logs"
```

ทำไมต้องผ่าน wrapper ไม่ชี้ exe ตรง ๆ

1. `cd` เข้าโฟลเดอร์ exe ก่อน — App.config และ path แบบ relative จึง resolve เหมือนตอนรันมือ
2. เก็บ stdout/stderr ลงไฟล์พร้อม timestamp — รอบที่ล้มเหลวมีหลักฐานทิ้งไว้
3. คืน exit code ของ exe ให้ Task Scheduler — ค่า `Last Run Result` จึงมีความหมาย
   (exit code = จำนวนฉบับที่ล้มเหลว, `0x0` = สำเร็จหมด)

---

## 6. Windows Task Scheduler

### 6.1 หลักการ

Windows มี service ชื่อ Task Scheduler รันอยู่ตั้งแต่บูต หน้าที่คือถือรายการ task
(เก็บเป็น XML ใน `C:\Windows\System32\Tasks\` — อยู่ถาวร รีบูตแล้วยังอยู่) แล้วคอยดูนาฬิกา
พอถึงเวลาก็สร้าง process ใหม่ รอจนจบ เก็บ exit code แล้วคำนวณรอบถัดไป

**ไม่มีอะไรของเราค้างในหน่วยความจำ** — โปรแกรมเกิด ทำงาน ตาย จบ จึงไม่มี memory leak
และทนต่อการรีสตาร์ทเครื่อง

งานส่งอีเมลใช้แนวคิด **polling**: ตั้งให้ปลุกทุกชั่วโมง แล้วให้โปรแกรมตัดสินเองว่า
รอบนี้ถึงกำหนดของใครแล้ว เพราะวัน/เวลาของผู้รับแต่ละคนอยู่ในฐานข้อมูลและแก้ได้จากหน้าเว็บ
ถ้าจะให้ Task Scheduler รู้เอง ต้องไปแก้ task ทุกครั้งที่มีคนเปลี่ยนเวลา ซึ่งเปราะ

รอบที่ยังไม่ถึงกำหนดจะจบในเสี้ยววินาที ไม่กินทรัพยากร

กันพลาดสองชั้น

- **ไม่ตกรอบ** — `IsDue` ใช้เกณฑ์ "เลยเวลาที่กำหนดมาแล้วหรือยัง" ไม่ใช่ "ตรงชั่วโมงนี้พอดีไหม"
  เครื่องปิดตอน 08:00 วันที่ 3 พอเปิดวันที่ 5 รอบถัดไปจะส่งตามให้ทันที
- **ไม่ส่งซ้ำ** — `meta.ReportDeliveryLog` จำว่าเดือนนี้ส่งให้ใครสำเร็จแล้ว
  รอบชั่วโมงถัด ๆ ไปจะข้าม

สองชั้นนี้ทำให้งาน **idempotent** — รันซ้ำกี่ครั้งผลก็เท่าเดิม ปลุกบ่อยเท่าไรก็ปลอดภัย

### 6.2 Task ที่ต้องสร้าง

| task | คำสั่ง | ตาราง |
|---|---|---|
| ดึงค่า KPI | `KpiReport.Etl.exe run-all` | วันละครั้ง ตอนกลางคืน (เช่น 01:00) |
| ส่งรายงาน | `tools\send-report.cmd` | ทุกชั่วโมง |

งานดึงค่า KPI ต้องเสร็จก่อนงานส่ง — ตั้งดึงตอน 01:00 แล้วผู้รับตั้งเวลาส่งไว้เช้า
(ค่าเริ่มต้นวันที่ 3 เวลา 08:00) จึงมีช่องว่างพอ

### 6.3 สร้างด้วย command line (แนะนำ)

เปิด Command Prompt แบบ **Run as administrator**

```bat
REM งานส่งรายงาน — ทุกชั่วโมง
schtasks /create /tn "KPI Monthly Report - send" ^
  /tr "C:\KPI-Monthly-Report-project\tools\send-report.cmd" ^
  /sc hourly /mo 1 /st 00:05 ^
  /ru "DOMAIN\svc_kpi" /rp * ^
  /rl HIGHEST /f

REM งานดึงค่า KPI — วันละครั้ง 01:00
schtasks /create /tn "KPI Monthly Report - feed" ^
  /tr "\"C:\Apps\KpiReport.Etl\KpiReport.Etl.exe\" run-all" ^
  /sc daily /st 01:00 ^
  /ru "DOMAIN\svc_kpi" /rp * ^
  /rl HIGHEST /f
```

- `/ru` + `/rp *` — รันในฐานะบัญชี service และถามรหัสผ่าน (จะไม่แสดงบนหน้าจอ)
  การใส่ `/ru` ทำให้ task รันได้แม้ไม่มีใคร login อยู่ ซึ่งเป็นสิ่งที่ต้องการบน server
- ถ้าไม่ต้องการเก็บรหัสผ่านใน task ให้ใช้ `/ru "SYSTEM"` แทน (ไม่ต้องมี `/rp`)
  แต่ต้องไปสร้าง SQL login ให้ `NT AUTHORITY\SYSTEM` หรือ `DOMAIN\COMPUTERNAME$` ในข้อ 2.3

### 6.4 เพิ่มออปชันสำคัญ (ทำผ่าน GUI)

เปิด `taskschd.msc` → เลือก task → Properties

**แท็บ General**

- ติ๊ก **Run whether user is logged on or not** (ถ้าใช้ `/ru` ข้างบนจะติ๊กมาให้แล้ว)
- ติ๊ก **Run with highest privileges**

**แท็บ Settings**

- ติ๊ก **Run task as soon as possible after a scheduled start is missed**
  สำคัญมากถ้าเครื่องอาจปิด/รีบูตตอนถึงเวลา
- ติ๊ก **If the task fails, restart every:** `10 minutes`, up to `3` times
  ครอบเคสเน็ตหลุดหรือ SMTP ตอบช้าชั่วคราว
- **Stop the task if it runs longer than:** `1 hour`
- **If the task is already running:** `Do not start a new instance`
  กันสองรอบทับกันตอนผู้รับเยอะ

**แท็บ Conditions**

- **เอาติ๊กออก**: "Start the task only if the computer is on AC power"
  (ไม่งั้นบนโน้ตบุ๊กที่ใช้แบตจะไม่รัน)
- **เอาติ๊กออก**: "Stop if the computer switches to battery power"

### 6.5 ตรวจและทดสอบ

```bat
REM ดูสถานะ + Next Run Time + Last Run Result
schtasks /query /tn "KPI Monthly Report - send" /v /fo list

REM สั่งรันเดี๋ยวนี้ ไม่ต้องรอ
schtasks /run /tn "KPI Monthly Report - send"
```

แล้วตรวจ 3 ที่

1. `C:\Apps\KpiReport.Etl\logs\send-report.log` — output ภาษาไทยรายคน
2. `Last Run Result` — `0x0` = สำเร็จหมด (ค่านี้คือจำนวนฉบับที่ล้มเหลว)
3. หน้าเว็บ **Report emails → log การส่งทั้งหมด** — แถวใหม่ขึ้นเป็น "ตามรอบ"

ถ้าขึ้นว่า "เคยส่งเดือนนี้ไปแล้ว" หรือ "ยังไม่ถึงกำหนด" = ทำงานถูก ไม่ใช่ error

### 6.6 คำสั่งที่ใช้ดูแลภายหลัง

```bat
schtasks /change /tn "KPI Monthly Report - send" /disable   พักไว้ชั่วคราว
schtasks /change /tn "KPI Monthly Report - send" /enable    เปิดกลับ
schtasks /delete /tn "KPI Monthly Report - send" /f         ลบ
```

### 6.7 ทางเลือกแทน Task Scheduler

**SQL Server Agent Job** — เหมาะถ้ามี SQL Server Standard/Enterprise อยู่แล้ว
สร้าง job ที่มี step แบบ **CmdExec** เรียก `C:\Apps\KpiReport.Etl\KpiReport.Etl.exe send-report`
ตั้ง schedule รายชั่วโมง ข้อดีคือมีหน้าดู job history และแจ้งเตือนทางอีเมลในตัว
ข้อเสียคือ SQL Express ไม่มี Agent

**Windows Service** — ต้องเขียนเพิ่ม ไม่คุ้มกับงานขนาดนี้
เพราะ Task Scheduler ให้สิ่งเดียวกันโดยไม่ต้องดูแล process เอง

---

## 7. Checklist หลังติดตั้งเสร็จ

- [ ] `33_verify_employee_kpi.sql` ผ่านทุกข้อ
- [ ] เปิดเว็บได้ login ได้ และเปลี่ยนรหัสผ่าน Admin เริ่มต้นแล้ว
- [ ] หน้า Dashboard มีตัวเลข (ถ้าว่าง = ยังไม่ได้รัน `run-all` + `rollup`)
- [ ] หน้า Monitoring แสดงภาษาไทยถูกต้อง ไม่เป็นตัวอ่านไม่ออก
- [ ] `KpiReport.Etl.exe send-report --dry-run` แสดงผู้รับถูกต้อง
- [ ] ปุ่ม "ส่งเดี๋ยวนี้" ในหน้า Report emails ส่งได้จริง และขึ้นในหน้า log เป็น "กดส่งเอง"
- [ ] task ทั้งสองตัวมี `Next Run Time` และ `Last Run Result` = `0x0`
- [ ] ติ๊ก "Run task as soon as possible after a scheduled start is missed" แล้ว
- [ ] มีแผน backup ฐานข้อมูล (`KpiMonthlyReport` เก็บทั้งข้อมูล KPI และบัญชีผู้ใช้)

---

## 8. ไล่ปัญหา

### เว็บขึ้น 500.19 หรือ configuration error

ASP.NET 4.8 ยังไม่ได้ลงทะเบียนกับ IIS — รัน `aspnet_regiis.exe -i` ในข้อ 1

### เว็บขึ้น "Login failed for user 'IIS APPPOOL\KpiReport'"

ยังไม่ได้สร้าง SQL login ในข้อ 2.3 หรือชื่อ app pool ไม่ตรงกับที่สร้าง login ไว้

### ภาษาไทยในหน้าเว็บเป็นตัวอ่านไม่ออก (`เธ...`)

ไฟล์ `.cshtml` หรือ `.cs` บางไฟล์ถูกบันทึกแบบ UTF-8 **ไม่มี BOM** — compiler จะเดา
encoding เป็น ANSI codepage ของเครื่อง บันทึกใหม่เป็น "UTF-8 with signature"
แล้ว rebuild (`.editorconfig` ที่ root ตั้ง `charset = utf-8-bom` ป้องกันไว้แล้ว)

### `send-report` ขึ้น "ไม่พบ environment variable 'KPI_SMTP_PASSWORD'"

ยังไม่ได้ตั้งค่า หรือตั้งไว้คนละบัญชีกับที่ task ใช้รัน (ข้อ 4.3)
หรือตั้งแล้วแต่ยังไม่เปิด session ใหม่

### ถึงเวลาแล้วอีเมลไม่ออก

ไล่ตามลำดับ หยุดที่ข้อแรกที่เจอ

1. `schtasks /query` — task มีอยู่จริงและ `Next Run Time` มีค่าไหม
2. `send-report --dry-run` — โปรแกรมคิดว่าถึงกำหนดของใครแล้วบ้าง
3. ไม่มีเดือนใน `rpt.vw_ValidMonth` → ยังไม่ได้รัน `run-all` + `rollup`
4. ขึ้น "เคยส่งเดือนนี้ไปแล้ว" → ถูกต้องแล้ว ดูหน้า log ยืนยัน (ถ้าจะส่งซ้ำใช้ `--force`)
5. ผู้รับขึ้นป้าย "ระงับ" ในหน้าเว็บ → `IsActive = 0` หรือบัญชีที่ผูกไว้ถูกล็อก
6. ส่งแล้วไม่ถึงกล่องจดหมาย → ดูสถานะในหน้า log: "ล้มเหลว" จะมี error ของ SMTP ติดมา,
   "ค้าง" = โปรเซสตายก่อนบันทึกผล และตรวจว่า `Smtp.DeliveryMethod` ไม่ได้เป็น
   `SpecifiedPickupDirectory` (โหมดนั้นเขียนไฟล์ `.eml` ลงโฟลเดอร์ ไม่ส่งออกจากเครื่อง)

### Gmail / Office 365 ปฏิเสธการ login

ต้องใช้ **app password** ไม่ใช่รหัสผ่านบัญชี และบัญชีต้องเปิด 2FA ก่อนจึงจะสร้าง
app password ได้ ส่วน Office 365 อาจต้องให้ผู้ดูแลเปิด SMTP AUTH ให้ mailbox นั้นก่อน

---

## เอกสารที่เกี่ยวข้อง

| ไฟล์ | เนื้อหา |
|---|---|
| `sql\README.md` | รายละเอียดสคริปต์ฐานข้อมูลแต่ละไฟล์ และสถาปัตยกรรมข้อมูล |
| `tools\README-send-report-schedule.md` | เจาะลึกรอบส่งอีเมลและการไล่ปัญหาบนเครื่อง dev |
| `src\KpiReport.Web\design.md` | ระบบดีไซน์ของหน้าเว็บ |
