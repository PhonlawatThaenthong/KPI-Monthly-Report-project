using System;
using System.Configuration;

namespace KpiReport.Web.Infrastructure
{
    /// <summary>
    /// ค่าตั้งเกี่ยวกับความปลอดภัยของการเข้าสู่ระบบ อ่านจาก appSettings ใน Web.config
    ///
    /// หลักการของค่าตั้งต้น: **ปลอดภัยไว้ก่อนเมื่อไม่ใช่ Debug**
    /// build แบบ Release (ตัวที่เอาขึ้นเซิร์ฟเวอร์จริง) จะบังคับ HTTPS และไม่ seed
    /// บัญชีตัวอย่างให้อัตโนมัติ แม้ลืมใส่ค่าใน Web.config ก็ตาม
    /// ส่วนตอน Debug บนเครื่องตัวเองจะผ่อนให้ทำงานบน http://localhost ได้ตามปกติ
    ///
    /// ทุกค่าทับได้จาก Web.config เช่น
    ///   &lt;add key="Auth:SessionMinutes" value="30" /&gt;
    /// </summary>
    public static class AuthSettings
    {
#if DEBUG
        private const bool SecureDefault = false;
        private const bool SeedDefault = true;
#else
        private const bool SecureDefault = true;
        private const bool SeedDefault = false;
#endif

        /// <summary>
        /// อายุ session ก่อนต้อง login ใหม่ (นาที) — นับแบบ sliding คือขยับออกไปเรื่อย ๆ
        /// ตราบใดที่ยังใช้งานอยู่ ค่าตั้งต้น 60 นาที
        ///
        /// ของเดิมไม่ได้ตั้งเลย จึงใช้ค่า default ของ OWIN คือ 14 วัน
        /// ซึ่งยาวเกินไปสำหรับเว็บที่เปิดค้างบนเครื่องกลางในออฟฟิศ
        /// </summary>
        public static int SessionMinutes
        {
            get { return GetInt("Auth:SessionMinutes", 60); }
        }

        /// <summary>
        /// รอบตรวจว่า user ยังมีสิทธิ์อยู่หรือไม่ (นาที)
        /// ถ้า Admin เปลี่ยนรหัสผ่านหรือถอด role ให้ใคร คนนั้นจะหลุดภายในเวลานี้
        /// ค่าตั้งต้น 15 นาที (ของ template คือ 30)
        /// </summary>
        public static int ValidateIdentityMinutes
        {
            get { return GetInt("Auth:ValidateIdentityMinutes", 15); }
        }

        /// <summary>
        /// บังคับให้เว็บทั้งเว็บวิ่งผ่าน HTTPS และส่งคุกกี้เฉพาะบน HTTPS
        /// ถ้าไม่เปิด คุกกี้ auth จะวิ่งเป็น plain text ใครดักในวง LAN เดียวกันก็ขโมย session ได้
        /// </summary>
        public static bool RequireHttps
        {
            get { return GetBool("Auth:RequireHttps", SecureDefault); }
        }

        /// <summary>
        /// สร้างบัญชีตัวอย่าง (Admin/Manager/Viewer) ตอนแอปเริ่มทำงานหรือไม่
        /// ใช้ตอนพัฒนาและตอนเดโมเท่านั้น ห้ามเปิดบนเซิร์ฟเวอร์จริง
        /// </summary>
        public static bool SeedDemoUsers
        {
            get { return GetBool("Auth:SeedDemoUsers", SeedDefault); }
        }

        /// <summary>
        /// อีเมลของ Admin คนแรกของระบบจริง (production bootstrap)
        ///
        /// ตั้งค่านี้ (ไม่ใช่ความลับ เป็นแค่ที่อยู่อีเมล ใส่ตรงใน Web.config ได้)
        /// แล้วตอนแอปเริ่มทำงานครั้งแรก ถ้ายังไม่มีใครอยู่ role Admin เลย
        /// IdentitySeeder จะสร้างบัญชีนี้ให้พร้อม role Admin ด้วยรหัสผ่านสุ่มที่
        /// สร้างแล้วทิ้งทันที (ไม่ log ไม่เก็บที่ไหน ไม่มีใครรู้ค่า)
        ///
        /// วิธีเข้าระบบครั้งแรก: ไปหน้า "ลืมรหัสผ่าน" (/Account/ForgotPassword)
        /// แล้วกรอกอีเมลนี้ ระบบจะส่งลิงก์ตั้งรหัสผ่านใหม่ผ่าน SMTP ที่ตั้งไว้
        /// (Smtp.* ใน Web.config) — ใช้ path เดียวกับที่ผู้ใช้ทั่วไปกดลืมรหัสผ่าน
        /// เอง 100% ไม่มีโค้ดพิเศษแยกออกไป จึงได้ token/ลิงก์ที่ปลอดภัยระดับ
        /// เดียวกัน (หมดอายุใน 1 ชั่วโมง ตาม TokenLifespan ใน IdentityConfig.cs)
        ///
        /// เหตุผลที่ไม่ส่งอีเมลนี้ให้อัตโนมัติตอน Application_Start เอง:
        /// ตอนนั้นยังไม่มี OWIN/HTTP context จะสร้างลิงก์ callback ที่ถูกต้อง
        /// (Url.Action) และ token provider ที่ตรงกับที่ AccountController ใช้
        /// ตรวจสอบตอนกดลิงก์ไม่ได้ ปล่อยให้ผู้ดูแลกดเองครั้งแรกจึงปลอดภัยกว่า
        ///
        /// ทิ้งว่างไว้ (ค่าตั้งต้น) = ปิดฟีเจอร์นี้ ไม่ auto-create อะไร
        /// </summary>
        public static string InitialAdminEmail
        {
            get { return ConfigurationManager.AppSettings["Auth:InitialAdminEmail"]; }
        }

        /// <summary>
        /// รหัสผ่านของบัญชีตัวอย่าง — อ่านจาก environment variable เท่านั้น
        /// ไม่เก็บใน Web.config อีกต่อไป (เหมือน KPI_SMTP_PASSWORD ใน SmtpSettings.cs)
        ///
        /// ของเดิม hardcode ไว้ในโค้ด ซึ่งหลุดขึ้น GitHub ไปพร้อมกับ source
        /// รอบที่แล้วย้ายมาไว้ใน Web.config โดยเข้าใจผิดว่าไฟล์นั้นอยู่ใน .gitignore
        /// — จริง ๆ แล้วไม่ได้ ignore (และไม่ควร ignore ทั้งไฟล์ เพราะมี Debug/Release
        /// transform และค่าตั้งอื่นที่ไม่ใช่ความลับต้อง track ไว้) ตอนนี้จึงย้ายค่านี้
        /// ออกมาเป็น environment variable แทน ตั้งด้วย setx (เครื่อง dev) หรือ
        /// Application Pool environmentVariables (IIS) ชื่อ KPI_DEMO_PASSWORD
        ///
        /// ถ้าไม่ได้ตั้งค่าไว้ จะไม่ seed อะไรเลย (ไม่มีรหัสผ่าน default ให้เดา)
        /// </summary>
        public static string DemoPassword
        {
            get { return Environment.GetEnvironmentVariable("KPI_DEMO_PASSWORD"); }
        }

        // ---------------------------------------------------------------

        private static int GetInt(string key, int fallback)
        {
            int value;
            string raw = ConfigurationManager.AppSettings[key];
            return int.TryParse(raw, out value) && value > 0 ? value : fallback;
        }

        private static bool GetBool(string key, bool fallback)
        {
            bool value;
            string raw = ConfigurationManager.AppSettings[key];
            return bool.TryParse(raw, out value) ? value : fallback;
        }
    }
}
