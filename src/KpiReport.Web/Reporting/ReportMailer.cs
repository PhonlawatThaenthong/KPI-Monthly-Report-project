using System;
using System.Globalization;
using System.Linq;
using System.Text;
using KpiReport.Shared.Mail;
using KpiReport.Web.Repositories;

namespace KpiReport.Web.Reporting
{
    /// <summary>ผู้รับ 1 รายที่จะส่งรายงานให้ — ตัดจากที่มาว่าเป็นแถวของ ETL หรือของเว็บ</summary>
    public class ReportRecipient
    {
        public string Email { get; set; }
        public string DisplayName { get; set; }

        /// <summary>null = ได้รายงานภาพรวมทั้งบริษัท + แยกรายแผนก</summary>
        public int? DepartmentId { get; set; }

        public string DepartmentName { get; set; }

        public bool IsCompanyWide
        {
            get { return !DepartmentId.HasValue; }
        }

        /// <summary>ชื่อขอบเขตที่จะพิมพ์บนหัวรายงานและใช้เป็นคีย์กันส่งซ้ำ</summary>
        public string ScopeLabel
        {
            get { return IsCompanyWide ? "All Departments" : (DepartmentName ?? "#" + DepartmentId); }
        }
    }

    /// <summary>ผลของการส่ง 1 ฉบับ</summary>
    public class ReportMailResult
    {
        /// <summary>false = ข้ามเพราะไม่มีข้อมูล KPI ของเดือนนั้น (ไม่ได้เขียน log)</summary>
        public bool HasData { get; set; }

        public int KpiCount { get; set; }
        public int PdfBytes { get; set; }
        public string FileName { get; set; }
        public string MonthLabel { get; set; }
    }

    /// <summary>
    /// สร้าง PDF รายงานรายเดือนของผู้รับ 1 ราย ส่งอีเมล และบันทึกลง
    /// meta.ReportDeliveryLog
    ///
    /// ไฟล์นี้อยู่ในโปรเจกต์เว็บแต่ถูกผูกเข้า ETL ด้วย csproj Link เหมือน
    /// PdfReportBuilder — งานส่งอัตโนมัติรายเดือน (MonthlyReportJob) และ
    /// ปุ่ม "ส่งเดี๋ยวนี้" ในหน้าผู้รับรายงานจึงได้ไฟล์ เนื้อเมล และ log
    /// ชุดเดียวกันเป๊ะ
    ///
    /// กติกาสำคัญที่ต้องอยู่ในนี้ที่เดียว: ส่วนแยกรายแผนกแนบให้เฉพาะผู้รับ
    /// ที่มีขอบเขตทั้งบริษัท — คนที่ผูกกับแผนกเดียวต้องไม่เห็นตัวเลขของ
    /// แผนกอื่นแม้จะอยู่ในไฟล์แนบ หลักเดียวกับ CanViewAllDepartments ในเว็บ
    /// </summary>
    public class ReportMailer
    {
        private readonly KpiRepository _kpiRepo;
        private readonly ReportDeliveryRepository _deliveryRepo;
        private readonly SmtpMailSender _mail;

        public ReportMailer(string connectionString)
        {
            _kpiRepo = new KpiRepository(connectionString);
            _deliveryRepo = new ReportDeliveryRepository(connectionString);
            _mail = new SmtpMailSender();
        }

        /// <summary>
        /// ชื่อรายงานของรอบส่งอัตโนมัติ ใช้เป็นคีย์กันส่งซ้ำใน meta.ReportDeliveryLog
        /// </summary>
        public static string ReportNameFor(ReportRecipient recipient)
        {
            return "KPI_Monthly:" + (recipient.IsCompanyWide ? "ALL" : recipient.ScopeLabel);
        }

        /// <summary>
        /// ชื่อรายงานของการกดส่งด้วยมือ — ตั้งใจให้คนละคีย์กับรอบอัตโนมัติ
        ///
        /// การส่งมือคือ "ขอดูฉบับนี้เดี๋ยวนี้" ไม่ใช่การส่งแทนรอบประจำเดือน
        /// ถ้าใช้คีย์เดียวกัน AlreadySent จะมองว่าเดือนนี้ส่งไปแล้ว แล้วรอบ
        /// อัตโนมัติจะข้ามผู้รับรายนั้นทั้งเดือน ซึ่งไม่ใช่สิ่งที่ตั้งใจ
        /// แยกคีย์ไว้ log ก็ยังแยกออกว่าฉบับไหนมาจากใครกด
        /// </summary>
        public static string ManualReportNameFor(ReportRecipient recipient)
        {
            return "KPI_Monthly_Manual:" + (recipient.IsCompanyWide ? "ALL" : recipient.ScopeLabel);
        }

        /// <summary>
        /// ส่งรายงานของเดือนที่ระบุให้ผู้รับรายนี้
        ///
        /// reportName เป็นคีย์ที่จะบันทึกลง meta.ReportDeliveryLog ผู้เรียกเป็น
        /// คนเลือกเอง (ReportNameFor สำหรับรอบอัตโนมัติ ManualReportNameFor
        /// สำหรับปุ่มส่งเดี๋ยวนี้) เพราะคีย์นี้เป็นตัวตัดสินการกันส่งซ้ำ
        ///
        /// dryRun = สร้าง PDF ให้ดูขนาดและจำนวน KPI แต่ไม่ส่งและไม่เขียน log
        /// โยน exception ต่อเมื่อส่งไม่สำเร็จ (บันทึก FAILED ลง log ให้ก่อนแล้ว)
        /// </summary>
        public ReportMailResult Send(ReportRecipient recipient, int monthKey, string reportName,
                                     string generatedBy, bool dryRun)
        {
            // -99 คือรหัส "ทุกแผนก" ตัวเดียวกับที่หน้า Dashboard ใช้
            int effectiveDepartmentId = recipient.DepartmentId ?? -99;

            var rows = _kpiRepo.GetDashboard(monthKey, effectiveDepartmentId)
                               .OrderBy(r => r.SortOrder)
                               .ToList();

            if (rows.Count == 0)
                return new ReportMailResult { HasData = false };

            var data = new KpiReportData
            {
                MonthKey = monthKey,
                MonthLabel = rows.First().MonthLabel ?? monthKey.ToString(CultureInfo.InvariantCulture),
                ScopeLabel = recipient.ScopeLabel,
                GeneratedBy = generatedBy,
                GeneratedAt = DateTime.Now,
                Rows = rows
            };

            // ส่วนแยกรายแผนกให้เฉพาะผู้รับที่มีขอบเขตทั้งบริษัท
            if (recipient.IsCompanyWide)
            {
                var deptRows = _kpiRepo.GetByDepartment(monthKey)
                                       .OrderBy(r => r.DepartmentName)
                                       .ThenBy(r => r.SortOrder)
                                       .ToList();

                if (deptRows.Count > 0) data.DepartmentRows = deptRows;
            }

            byte[] pdf = PdfReportBuilder.Build(data);
            string fileName = data.FileName("pdf");

            var result = new ReportMailResult
            {
                HasData = true,
                KpiCount = rows.Count,
                PdfBytes = pdf.Length,
                FileName = fileName,
                MonthLabel = data.MonthLabel
            };

            if (dryRun) return result;

            string subject = "[HR KPI] รายงานประจำเดือน " + data.MonthLabel + " — " + recipient.ScopeLabel;

            // จอง log ก่อนส่ง ถ้าโปรเซสตายกลางทางจะยังเหลือร่องรอยว่าค้างที่ใคร
            long deliveryId = _deliveryRepo.LogPending(
                monthKey, reportName, "PDF", recipient.Email, pdf.LongLength);

            try
            {
                _mail.SendWithAttachment(
                    recipient.Email, recipient.DisplayName, subject,
                    BuildBody(data, recipient), pdf, fileName, "application/pdf");

                _deliveryRepo.MarkSent(deliveryId);
                return result;
            }
            catch (Exception ex)
            {
                // บันทึกสาเหตุลง log ก่อน แล้วค่อยโยนต่อให้ผู้เรียกจัดการ
                _deliveryRepo.MarkFailed(deliveryId, Trim(ex.Message, 1000));
                throw;
            }
        }

        /// <summary>
        /// เนื้ออีเมลตั้งใจให้สั้น: บอกว่าเป็นเดือนไหน ขอบเขตไหน
        /// และภาพรวมสถานะพอให้ตัดสินใจได้ว่าต้องเปิดไฟล์ดูด่วนหรือไม่
        /// รายละเอียดทั้งหมดอยู่ใน PDF ที่แนบไป
        /// </summary>
        private static string BuildBody(KpiReportData data, ReportRecipient recipient)
        {
            var sb = new StringBuilder();

            sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1b2430;\">");
            sb.Append("<p>เรียน ").Append(Encode(recipient.DisplayName ?? recipient.Email)).Append("</p>");

            sb.Append("<p>รายงาน KPI ประจำเดือน <strong>").Append(Encode(data.MonthLabel))
              .Append("</strong> ขอบเขต <strong>").Append(Encode(data.ScopeLabel))
              .Append("</strong> แนบมาในไฟล์ PDF</p>");

            sb.Append("<table style=\"border-collapse:collapse;font-size:13px;margin:14px 0;\">");
            AppendStat(sb, "KPI ทั้งหมด", data.Rows.Count, "#0b2545");
            AppendStat(sb, "เข้าเป้า", data.CountGreen, "#1a7f37");
            AppendStat(sb, "เฝ้าระวัง", data.CountYellow, "#9a6700");
            AppendStat(sb, "ต่ำกว่าเป้า", data.CountRed, "#b42318");
            sb.Append("</table>");

            if (data.CountRed > 0)
            {
                sb.Append("<p style=\"color:#b42318;\">มี KPI ที่ต่ำกว่าเป้า ")
                  .Append(data.CountRed)
                  .Append(" ตัว รายละเอียดอยู่ในไฟล์แนบ</p>");
            }

            sb.Append("<p style=\"color:#5c6b7a;font-size:12px;margin-top:20px;\">")
              .Append("อีเมลฉบับนี้ส่งอัตโนมัติจากระบบ HR KPI Monitoring ")
              .Append("หากต้องการเปลี่ยนแปลงการรับรายงาน กรุณาติดต่อทีม HR Analytics")
              .Append("</p>");

            sb.Append("</div>");
            return sb.ToString();
        }

        private static void AppendStat(StringBuilder sb, string label, int value, string color)
        {
            sb.Append("<tr>")
              .Append("<td style=\"padding:4px 16px 4px 0;color:#5c6b7a;\">").Append(Encode(label)).Append("</td>")
              .Append("<td style=\"padding:4px 0;font-weight:700;color:").Append(color).Append(";\">")
              .Append(value.ToString(CultureInfo.InvariantCulture))
              .Append("</td>")
              .Append("</tr>");
        }

        /// <summary>
        /// ชื่อแผนกและชื่อผู้รับมาจากฐานข้อมูล ต้อง escape ก่อนใส่ลง HTML เสมอ
        /// ไม่งั้นชื่อที่มี &lt; หรือ &amp; จะทำให้อีเมลเพี้ยน
        /// </summary>
        private static string Encode(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : System.Net.WebUtility.HtmlEncode(value);
        }

        private static string Trim(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= max ? value : value.Substring(0, max);
        }
    }
}
