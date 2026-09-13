using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Net.Mail;

namespace KpiReport.Shared.Mail
{
    /// <summary>
    /// ส่งอีเมลผ่าน SMTP โดยอ่านค่าจาก SmtpSettings (appSettings "Smtp.*")
    /// ของโปรเจกต์ที่เรียกใช้ (App.config ของ ETL หรือ Web.config ของเว็บ)
    /// รหัสผ่านอ่านจาก environment variable KPI_SMTP_PASSWORD เท่านั้น
    /// ดู SmtpSettings.cs
    ///
    /// ไฟล์นี้อยู่ใน src/Shared และถูกผูกเข้าทั้งสองโปรเจกต์ด้วย csproj Link
    /// ไม่ได้ copy — แก้ที่เดียวมีผลทั้งงานส่งรายงานรายเดือน (ETL)
    /// และงานส่งลิงก์ตั้งรหัสผ่านใหม่ (เว็บ)
    ///
    /// *** ค่าตั้ง SMTP ต้องอยู่ทั้ง App.config และ Web.config ***
    /// เปลี่ยนเซิร์ฟเวอร์เมลเมื่อไหร่ ต้องแก้ทั้งสองไฟล์
    ///
    /// ทดสอบโดยไม่มีเซิร์ฟเวอร์จริงได้ ตั้ง Smtp.DeliveryMethod เป็น
    /// SpecifiedPickupDirectory แล้วเมลจะถูกเขียนเป็นไฟล์ .eml
    /// ลงโฟลเดอร์ที่ระบุแทนการส่งออกไปจริง
    /// </summary>
    public class SmtpMailSender
    {
        private readonly string _fromAddress;
        private readonly string _fromName;

        public SmtpMailSender()
        {
            _fromAddress = ConfigurationManager.AppSettings["Report:FromAddress"];
            _fromName = ConfigurationManager.AppSettings["Report:FromName"] ?? "HR KPI Monitoring System";

            if (string.IsNullOrWhiteSpace(_fromAddress))
                throw new ConfigurationErrorsException(
                    "ยังไม่ได้ตั้ง appSetting 'Report:FromAddress' ใน App.config");
        }

        /// <summary>
        /// ส่งอีเมล 1 ฉบับพร้อมไฟล์แนบ 1 ไฟล์
        /// โยน exception ออกไปให้ผู้เรียกจัดการ เพื่อให้บันทึกลง
        /// meta.ReportDeliveryLog ได้ว่าฉบับไหนล้มเหลวเพราะอะไร
        /// </summary>
        public void SendWithAttachment(
            string toAddress,
            string toDisplayName,
            string subject,
            string htmlBody,
            byte[] attachment,
            string attachmentFileName,
            string attachmentContentType)
        {
            using (var message = new MailMessage())
            {
                message.From = new MailAddress(_fromAddress, _fromName);
                message.To.Add(string.IsNullOrWhiteSpace(toDisplayName)
                    ? new MailAddress(toAddress)
                    : new MailAddress(toAddress, toDisplayName));

                message.Subject = subject;
                message.Body = htmlBody;
                message.IsBodyHtml = true;

                // MemoryStream ต้องมีชีวิตอยู่จนกว่าจะส่งเสร็จ
                // จึงผูกอายุไว้กับ using ของ message ไม่ใช่ปิดทิ้งทันที
                using (var stream = new MemoryStream(attachment))
                {
                    var file = new Attachment(stream, attachmentFileName, attachmentContentType);
                    message.Attachments.Add(file);

                    using (var client = CreateClient())
                    {
                        client.Send(message);
                    }
                }
            }
        }

        /// <summary>
        /// ส่งอีเมลข้อความล้วน ไม่มีไฟล์แนบ
        ///
        /// ใช้กับลิงก์ตั้งรหัสผ่านใหม่ ซึ่งฝั่งเว็บเรียกผ่าน
        /// EmailService (IIdentityMessageService) ใน IdentityConfig.cs
        /// </summary>
        public void Send(string toAddress, string subject, string htmlBody)
        {
            using (var message = new MailMessage())
            {
                message.From = new MailAddress(_fromAddress, _fromName);
                message.To.Add(new MailAddress(toAddress));
                message.Subject = subject;
                message.Body = htmlBody;
                message.IsBodyHtml = true;

                using (var client = CreateClient())
                {
                    client.Send(message);
                }
            }
        }

        /// <summary>
        /// สร้าง SmtpClient จาก SmtpSettings
        ///
        /// SpecifiedPickupDirectory (โหมดทดสอบ) : ไม่ต้องมี credentials เลย
        /// สร้างโฟลเดอร์ปลายทางให้ล่วงหน้าด้วย เพราะถ้าไม่มีโฟลเดอร์
        /// SmtpClient จะโยน error ที่อ่านไม่รู้เรื่อง
        ///
        /// Network (ใช้งานจริง) : ต้องมี Host/UserName ใน config และ
        /// KPI_SMTP_PASSWORD ใน environment variable — ดู SmtpSettings.Password
        /// </summary>
        private static SmtpClient CreateClient()
        {
            var client = new SmtpClient();

            if (string.Equals(SmtpSettings.DeliveryMethod, "SpecifiedPickupDirectory", StringComparison.OrdinalIgnoreCase))
            {
                client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;

                string path = Path.GetFullPath(SmtpSettings.PickupDirectory ?? Path.GetTempPath());
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                client.PickupDirectoryLocation = path;

                return client;
            }

            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.Host = SmtpSettings.Host;
            client.Port = SmtpSettings.Port;
            client.EnableSsl = true;
            client.Credentials = new NetworkCredential(SmtpSettings.UserName, SmtpSettings.Password);

            return client;
        }

        /// <summary>ไว้พิมพ์บอกตอนรันว่ากำลังส่งจริงหรือแค่เขียนไฟล์ทดสอบ</summary>
        public static string DescribeDeliveryMode()
        {
            if (string.Equals(SmtpSettings.DeliveryMethod, "SpecifiedPickupDirectory", StringComparison.OrdinalIgnoreCase))
                return "เขียนไฟล์ .eml ลง " + SmtpSettings.PickupDirectory + " (ไม่ได้ส่งออกจริง)";

            return "ส่งผ่าน SMTP " + SmtpSettings.Host + ":" + SmtpSettings.Port;
        }
    }
}
