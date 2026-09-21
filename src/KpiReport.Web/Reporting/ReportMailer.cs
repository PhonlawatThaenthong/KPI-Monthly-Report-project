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

        /// <summary>
        /// รายการ DepartmentId คั่นด้วย comma ('3,5,8')
        /// null/ว่าง = ได้รายงานภาพรวมทั้งบริษัท + แยกรายแผนก
        /// </summary>
        public string DepartmentIds { get; set; }

        /// <summary>ชื่อแผนกที่เลือกไว้ คั่นด้วย comma (ใช้แสดงผลอย่างเดียว)</summary>
        public string DepartmentNames { get; set; }

        public int DepartmentCount { get; set; }

        public bool IsCompanyWide
        {
            get { return DepartmentCount == 0 || string.IsNullOrEmpty(DepartmentIds); }
        }

        /// <summary>ชื่อขอบเขตที่จะพิมพ์บนหัวรายงานและใช้เป็นคีย์กันส่งซ้ำ</summary>
        public string ScopeLabel
        {
            get
            {
                if (IsCompanyWide) return "All Departments";
                return DepartmentNames ?? DepartmentIds;
            }
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
        ///
        /// subscriptionId / triggerType / triggeredBy เป็นข้อมูลของ log อย่างเดียว
        /// ไม่มีผลต่อไฟล์หรือเนื้อเมล — มีไว้ให้หน้า log ตอบได้ว่าฉบับนี้มาจาก
        /// รอบอัตโนมัติหรือมีคนกดส่ง และเป็นของผู้รับรายไหน
        /// </summary>
        public ReportMailResult Send(ReportRecipient recipient, int monthKey, string reportName,
                                     string generatedBy, bool dryRun,
                                     int? subscriptionId = null, string triggerType = "SCHEDULED",
                                     string triggeredBy = null)
        {
            // -99 คือรหัส "ทุกแผนก" ตัวเดียวกับที่หน้า Dashboard ใช้
            //
            // ผู้รับที่เลือกไว้หลายแผนก ได้แถวแยกรายแผนกมาเลยในครั้งเดียว
            // ไม่วนเรียกทีละแผนกแล้วเอามาต่อกันเอง เพราะจะได้ลำดับที่เพี้ยน
            // และยิง query ซ้ำโดยไม่จำเป็น
            bool multiDepartment = !recipient.IsCompanyWide;

            var rows = multiDepartment
                ? _kpiRepo.GetDashboardMulti(monthKey, recipient.DepartmentIds)
                          .OrderBy(r => r.DepartmentName)
                          .ThenBy(r => r.SortOrder)
                          .ToList()
                : _kpiRepo.GetDashboard(monthKey, -99)
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
                Rows = rows,

                // ผู้รับที่เลือกไว้หลายแผนก: Rows เป็นข้อมูลแยกรายแผนกอยู่แล้ว
                // บอก builder ไว้ จะได้ไม่พิมพ์ตาราง KPI แบบแบนซ้ำอีกชุด
                RowsAreDepartmentBreakdown = multiDepartment && recipient.DepartmentCount > 1
            };

            if (recipient.IsCompanyWide)
            {
                // ขอบเขตทั้งบริษัท -> แนบส่วนแยกรายแผนกของทุกแผนกให้ด้วย
                var deptRows = _kpiRepo.GetByDepartment(monthKey)
                                       .OrderBy(r => r.DepartmentName)
                                       .ThenBy(r => r.SortOrder)
                                       .ToList();

                if (deptRows.Count > 0) data.DepartmentRows = deptRows;
            }
            else if (recipient.DepartmentCount > 1)
            {
                // เลือกไว้หลายแผนก -> ส่วนแยกรายแผนกมีเฉพาะแผนกที่เลือกเท่านั้น
                // ผู้รับต้องไม่เห็นตัวเลขของแผนกที่ไม่ได้อยู่ในขอบเขตของตัวเอง
                data.DepartmentRows = rows;
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
                monthKey, reportName, "PDF", recipient.Email, pdf.LongLength,
                subscriptionId, triggerType, triggeredBy, recipient.ScopeLabel);

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
        ///
        /// จัดด้วยตาราง (ไม่ใช้ flex/grid) และ inline style ล้วน เพราะต้อง
        /// เรนเดอร์ถูกทั้งใน Gmail และ Outlook desktop (Word engine) — ทั้งคู่
        /// ตัด &lt;style&gt;/คลาส CSS ทิ้งเป็นปกติ
        /// </summary>
        private static string BuildBody(KpiReportData data, ReportRecipient recipient)
        {
            const string borderColor = "#e3e8ee";
            const string mutedColor = "#5c6b7a";
            const string textColor = "#1b2430";

            var sb = new StringBuilder();

            sb.Append("<div style=\"background:#eef1f5;padding:32px 16px;font-family:'Segoe UI',Arial,sans-serif;\">");
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" ")
              .Append("style=\"max-width:600px;margin:0 auto;background:#ffffff;border:1px solid ")
              .Append(borderColor).Append(";border-radius:8px;\">");

            // หัวเอกสาร
            sb.Append("<tr><td style=\"background:#0b2545;padding:22px 28px;border-radius:8px 8px 0 0;\">");
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr>");
            sb.Append("<td style=\"color:#ffffff;font-size:16px;font-weight:700;letter-spacing:.2px;\">")
              .Append("HR KPI Monitoring System</td>");
            sb.Append("<td style=\"color:#aebdcf;font-size:12px;text-align:right;vertical-align:bottom;\">")
              .Append("รายงานประจำเดือน</td>");
            sb.Append("</tr></table></td></tr>");

            // คำทักทาย + สรุปหัวเรื่อง
            sb.Append("<tr><td style=\"padding:28px 28px 4px 28px;\">");
            sb.Append("<p style=\"margin:0 0 16px 0;font-size:14px;color:").Append(textColor).Append(";\">เรียน ")
              .Append(Encode(recipient.DisplayName ?? recipient.Email)).Append("</p>");
            sb.Append("<p style=\"margin:0;font-size:14px;color:").Append(textColor).Append(";line-height:1.7;\">")
              .Append("รายงาน KPI ประจำเดือน <strong>").Append(Encode(data.MonthLabel))
              .Append("</strong> ขอบเขต <strong>").Append(Encode(data.ScopeLabel))
              .Append("</strong> รายละเอียดฉบับเต็มแนบมาในไฟล์ PDF</p>");
            sb.Append("</td></tr>");

            // การ์ดสรุปสถานะ KPI
            sb.Append("<tr><td style=\"padding:20px 28px 4px 28px;\">");
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"8\"><tr>");
            AppendStatCell(sb, "KPI ทั้งหมด", data.Rows.Count, "#0b2545", "#f4f6f8", borderColor);
            AppendStatCell(sb, "เข้าเป้า", data.CountGreen, "#1a7f37", "#eef8f0", borderColor);
            AppendStatCell(sb, "เฝ้าระวัง", data.CountYellow, "#9a6700", "#fdf6e8", borderColor);
            AppendStatCell(sb, "ต่ำกว่าเป้า", data.CountRed, "#b42318", "#fbeae8", borderColor);
            sb.Append("</tr></table>");
            sb.Append("</td></tr>");

            if (data.CountRed > 0)
            {
                sb.Append("<tr><td style=\"padding:12px 28px 0 28px;\">");
                sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" ")
                  .Append("style=\"background:#fbeae8;border-left:3px solid #b42318;\"><tr>");
                sb.Append("<td style=\"padding:12px 16px;font-size:13px;color:#8f1f13;\">")
                  .Append("มี KPI ที่ต่ำกว่าเป้า <strong>").Append(data.CountRed)
                  .Append("</strong> ตัว รายละเอียดอยู่ในไฟล์แนบ")
                  .Append("</td></tr></table>");
                sb.Append("</td></tr>");
            }

            // ท้ายเอกสาร
            sb.Append("<tr><td style=\"padding:24px 28px 0 28px;\">");
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" ")
              .Append("style=\"border-top:1px solid ").Append(borderColor).Append(";\"><tr>")
              .Append("<td style=\"padding-top:16px;font-size:12px;color:").Append(mutedColor).Append(";line-height:1.7;\">")
              .Append("อีเมลฉบับนี้ส่งอัตโนมัติจากระบบ HR KPI Monitoring หากต้องการเปลี่ยนแปลงการรับรายงาน ")
              .Append("กรุณาติดต่อทีม HR Analytics")
              .Append("</td></tr></table>");
            sb.Append("</td></tr>");

            sb.Append("<tr><td style=\"padding:10px 28px 24px 28px;font-size:11px;color:#9aa7b4;\">")
              .Append("© ").Append(data.GeneratedAt.Year.ToString(CultureInfo.InvariantCulture))
              .Append(" HR Analytics Team — ระบบรายงาน KPI อัตโนมัติ")
              .Append("</td></tr>");

            sb.Append("</table></div>");
            return sb.ToString();
        }

        private static void AppendStatCell(StringBuilder sb, string label, int value, string color,
                                            string backgroundColor, string borderColor)
        {
            sb.Append("<td style=\"width:25%;padding:14px 6px;text-align:center;background:")
              .Append(backgroundColor).Append(";border:1px solid ").Append(borderColor)
              .Append(";border-radius:6px;\">")
              .Append("<div style=\"font-size:22px;font-weight:700;color:").Append(color).Append(";\">")
              .Append(value.ToString(CultureInfo.InvariantCulture))
              .Append("</div>")
              .Append("<div style=\"font-size:11px;color:#5c6b7a;margin-top:4px;\">")
              .Append(Encode(label))
              .Append("</div>")
              .Append("</td>");
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
