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
        /// รหัสผ่านของบัญชีตัวอย่าง — ต้องมาจาก Web.config เท่านั้น
        ///
        /// ของเดิม hardcode ไว้ในโค้ด ซึ่งหลุดขึ้น GitHub ไปพร้อมกับ source
        /// ย้ายมาไว้ที่นี่เพราะ Web.config อยู่ใน .gitignore อยู่แล้ว
        /// ถ้าไม่ได้ตั้งค่าไว้ จะไม่ seed อะไรเลย (ไม่มีรหัสผ่าน default ให้เดา)
        /// </summary>
        public static string DemoPassword
        {
            get { return ConfigurationManager.AppSettings["Auth:DemoPassword"]; }
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
