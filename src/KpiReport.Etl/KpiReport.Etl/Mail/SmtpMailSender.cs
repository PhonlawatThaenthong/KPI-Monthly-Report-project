using System;
using System.Configuration;
using System.IO;
using System.Net.Mail;

namespace KpiReport.Etl.Mail
{
    /// <summary>
    /// ส่งอีเมลผ่าน SMTP โดยอ่านค่าจาก &lt;system.net&gt;&lt;mailSettings&gt; ใน App.config
    ///
    /// ตั้งใจให้เป็นชั้นบาง ๆ ครอบ SmtpClient ไม่ใส่ logic อะไรเพิ่ม
    /// เพราะฝั่งเว็บจะต้องใช้ตัวเดียวกันนี้ตอนทำ forgot-password ทางอีเมล
    /// ถ้าถึงตอนนั้น ให้ย้ายไฟล์นี้ไป class library กลางแล้วอ้างจากทั้งสองฝั่ง
    ///
    /// ทดสอบโดยไม่มีเซิร์ฟเวอร์จริงได้ ตั้ง deliveryMethod เป็น
    /// SpecifiedPickupDirectory ใน App.config แล้วเมลจะถูกเขียนเป็นไฟล์ .eml
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

                    using (var client = new SmtpClient())
                    {
                        EnsurePickupDirectoryExists(client);
                        client.Send(message);
                    }
                }
            }
        }

        /// <summary>
        /// โหมดทดสอบแบบเขียนไฟล์ .eml : ถ้าโฟลเดอร์ปลายทางยังไม่มี
        /// SmtpClient จะโยน error ที่อ่านไม่รู้เรื่อง สร้างให้ล่วงหน้าเลยดีกว่า
        /// </summary>
        private static void EnsurePickupDirectoryExists(SmtpClient client)
        {
            if (client.DeliveryMethod != SmtpDeliveryMethod.SpecifiedPickupDirectory) return;
            if (string.IsNullOrWhiteSpace(client.PickupDirectoryLocation)) return;

            string path = Path.GetFullPath(client.PickupDirectoryLocation);
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            client.PickupDirectoryLocation = path;
        }

        /// <summary>ไว้พิมพ์บอกตอนรันว่ากำลังส่งจริงหรือแค่เขียนไฟล์ทดสอบ</summary>
        public static string DescribeDeliveryMode()
        {
            using (var client = new SmtpClient())
            {
                if (client.DeliveryMethod == SmtpDeliveryMethod.SpecifiedPickupDirectory)
                    return "เขียนไฟล์ .eml ลง " + client.PickupDirectoryLocation + " (ไม่ได้ส่งออกจริง)";

                return "ส่งผ่าน SMTP " + client.Host + ":" + client.Port;
            }
        }
    }
}
