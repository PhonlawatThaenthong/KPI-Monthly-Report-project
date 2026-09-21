using System;
using System.Configuration;

namespace KpiReport.Shared.Mail
{
    /// <summary>
    /// ค่าตั้ง SMTP อ่านจาก appSettings ("Smtp.*") ของโปรเจกต์ที่เรียกใช้
    /// (App.config ของ ETL หรือ Web.config ของเว็บ) — ยกเว้น password
    ///
    /// รหัสผ่านไม่เก็บใน config อีกต่อไป อ่านจาก environment variable
    /// KPI_SMTP_PASSWORD เท่านั้น เพื่อไม่ให้หลุดขึ้น source control
    /// (ตั้งด้วย setx บนเครื่อง dev หรือ Application Pool environmentVariables บน IIS)
    ///
    /// ตอน DeliveryMethod = SpecifiedPickupDirectory (โหมดทดสอบ เขียน .eml ลงไฟล์)
    /// ไม่ต้องมี password เลย เพราะไม่ได้เชื่อมต่อเซิร์ฟเวอร์เมลจริง
    /// </summary>
    public static class SmtpSettings
    {
        public static string DeliveryMethod
        {
            get { return Get("Smtp.DeliveryMethod", "SpecifiedPickupDirectory"); }
        }

        public static string PickupDirectory
        {
            get { return Get("Smtp.PickupDirectory", null); }
        }

        public static string Host
        {
            get { return Get("Smtp.Host", null); }
        }

        public static int Port
        {
            get
            {
                int value;
                return int.TryParse(Get("Smtp.Port", null), out value) && value > 0 ? value : 25;
            }
        }

        public static string From
        {
            get { return Get("Smtp.From", null); }
        }

        public static string UserName
        {
            get { return Get("Smtp.UserName", null); }
        }

        /// <summary>
        /// รหัสผ่าน SMTP จาก environment variable KPI_SMTP_PASSWORD
        /// โยน exception ถ้าไม่ได้ตั้งค่าไว้ — ใช้เฉพาะตอน DeliveryMethod = Network
        /// </summary>
        public static string Password
        {
            get
            {
                string value = Environment.GetEnvironmentVariable("KPI_SMTP_PASSWORD");
                if (string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException(
                        "ไม่พบ environment variable 'KPI_SMTP_PASSWORD' " +
                        "ตั้งค่าด้วย setx (เครื่อง dev) หรือ Application Pool " +
                        "environmentVariables (IIS) ก่อนส่งอีเมลผ่าน SMTP Network");

                return value;
            }
        }

        private static string Get(string key, string fallback)
        {
            string raw = ConfigurationManager.AppSettings[key];
            return string.IsNullOrEmpty(raw) ? fallback : raw;
        }
    }
}
