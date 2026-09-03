using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using KpiReport.Etl.Mail;
using KpiReport.Web.Models;
using KpiReport.Web.Reporting;
using KpiReport.Web.Repositories;

namespace KpiReport.Etl.Reports
{
    /// <summary>
    /// ส่งรายงาน KPI รายเดือนเป็น PDF ให้ผู้รับแต่ละคนตามขอบเขตของตัวเอง
    ///
    /// ทำไมงานนี้อยู่ในโปรเจกต์ ETL ไม่ใช่ในเว็บ
    /// ---------------------------------------------------------------
    /// IIS application pool มี idle timeout และ recycle ตัวเองเป็นระยะ
    /// งานตามตารางที่ฝากไว้ในเว็บจึงไม่รับประกันว่าจะได้รัน
    /// console app + Task Scheduler ตรงไปตรงมาและตรวจสอบง่ายกว่ามาก
    ///
    /// ตัวสร้าง PDF และตัวอ่านข้อมูลใช้ไฟล์ชุดเดียวกับเว็บ (ผูกด้วย Link
    /// ใน .csproj ไม่ได้ copy) ตัวเลขในอีเมลจึงตรงกับหน้าจอเสมอ
    /// แก้สูตรที่เดียวมีผลทั้งสองทาง
    /// </summary>
    public class MonthlyReportJob
    {
        private readonly ReportRepository _reportRepo;
        private readonly KpiRepository _kpiRepo;
        private readonly SmtpMailSender _mail;

        public MonthlyReportJob(string connectionString)
        {
            _reportRepo = new ReportRepository(connectionString);
            _kpiRepo = new KpiRepository(connectionString);
            _mail = new SmtpMailSender();
        }

        /// <summary>
        /// คืนจำนวนฉบับที่ล้มเหลว (0 = สำเร็จหมด) เพื่อให้ Program คืน exit code
        /// ที่ Task Scheduler เอาไปตั้งแจ้งเตือนได้
        /// </summary>
        public int Run(int? requestedMonthKey, bool dryRun, bool force)
        {
            int? monthKey = requestedMonthKey ?? _reportRepo.GetLatestMonthKey();

            if (monthKey == null)
            {
                Console.Error.WriteLine("   [ERROR] ยังไม่มีเดือนที่มีข้อมูลใน rpt.vw_ValidMonth — ยังไม่ได้รัน ETL หรือเปล่า");
                return 1;
            }

            var subscriptions = _reportRepo.GetActiveSubscriptions();
            if (subscriptions.Count == 0)
            {
                Console.WriteLine("   ไม่มีผู้รับที่ active ใน meta.ReportSubscription — ไม่มีอะไรให้ส่ง");
                return 0;
            }

            Console.WriteLine("   เดือน      : " + monthKey.Value);
            Console.WriteLine("   ผู้รับ      : " + subscriptions.Count + " ราย");
            Console.WriteLine("   ช่องทางส่ง : " + (dryRun ? "DRY RUN (ไม่ส่งจริง)" : SmtpMailSender.DescribeDeliveryMode()));
            Console.WriteLine();

            int failed = 0;
            int skipped = 0;
            int sent = 0;

            foreach (var sub in subscriptions)
            {
                string reportName = "KPI_Monthly:" + (sub.IsCompanyWide ? "ALL" : sub.ScopeLabel);

                if (!force && !dryRun && _reportRepo.AlreadySent(monthKey.Value, reportName, sub.Email))
                {
                    Console.WriteLine("   - ข้าม " + sub.Email + " (" + sub.ScopeLabel + ") — เคยส่งเดือนนี้ไปแล้ว");
                    skipped++;
                    continue;
                }

                try
                {
                    if (SendOne(sub, monthKey.Value, reportName, dryRun)) sent++;
                    else skipped++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("   ! ล้มเหลว " + sub.Email + " : " + ex.Message);
                    failed++;
                }
            }

            Console.WriteLine();
            Console.WriteLine("   สรุป: ส่ง " + sent + " · ข้าม " + skipped + " · ล้มเหลว " + failed);
            return failed;
        }

        // ---------------------------------------------------------------

        /// <summary>คืน false เมื่อข้ามเพราะไม่มีข้อมูลของเดือนนั้น</summary>
        private bool SendOne(ReportSubscription sub, int monthKey, string reportName, bool dryRun)
        {
            // -99 คือรหัส "ทุกแผนก" ตัวเดียวกับที่หน้า Dashboard ใช้
            int effectiveDepartmentId = sub.DepartmentId ?? -99;

            var rows = _kpiRepo.GetDashboard(monthKey, effectiveDepartmentId)
                               .OrderBy(r => r.SortOrder)
                               .ToList();

            if (rows.Count == 0)
            {
                Console.WriteLine("   - ข้าม " + sub.Email + " (" + sub.ScopeLabel + ") — ไม่มีข้อมูล KPI ของเดือนนี้");
                return false;
            }

            var data = new KpiReportData
            {
                MonthKey = monthKey,
                MonthLabel = rows.First().MonthLabel ?? monthKey.ToString(),
                ScopeLabel = sub.ScopeLabel,
                GeneratedBy = "Automated monthly delivery",
                GeneratedAt = DateTime.Now,
                Rows = rows
            };

            // ส่วนแยกรายแผนกให้เฉพาะผู้รับที่มีขอบเขตทั้งบริษัท
            // หลักเดียวกับ CanViewAllDepartments ในเว็บ — คนที่ผูกกับแผนกเดียว
            // ต้องไม่เห็นตัวเลขของแผนกอื่นแม้จะอยู่ในไฟล์แนบ
            if (sub.IsCompanyWide)
            {
                var deptRows = _kpiRepo.GetByDepartment(monthKey)
                                       .OrderBy(r => r.DepartmentName)
                                       .ThenBy(r => r.SortOrder)
                                       .ToList();

                if (deptRows.Count > 0) data.DepartmentRows = deptRows;
            }

            byte[] pdf = PdfReportBuilder.Build(data);
            string fileName = data.FileName("pdf");
            string subject = "[HR KPI] รายงานประจำเดือน " + data.MonthLabel + " — " + sub.ScopeLabel;

            if (dryRun)
            {
                Console.WriteLine("   . (dry run) " + sub.Email + " (" + sub.ScopeLabel + ") — "
                                  + rows.Count + " KPI, PDF " + pdf.Length / 1024 + " KB");
                return true;
            }

            // จอง log ก่อนส่ง ถ้าโปรเซสตายกลางทางจะยังเหลือร่องรอยว่าค้างที่ใคร
            long deliveryId = _reportRepo.LogPending(
                monthKey, reportName, "PDF", sub.Email, pdf.LongLength);

            try
            {
                _mail.SendWithAttachment(
                    sub.Email, sub.DisplayName, subject,
                    BuildBody(data, sub), pdf, fileName, "application/pdf");

                _reportRepo.MarkSent(deliveryId);
                Console.WriteLine("   + ส่งแล้ว " + sub.Email + " (" + sub.ScopeLabel + ") — " + fileName);
                return true;
            }
            catch (Exception ex)
            {
                // บันทึกสาเหตุลง log ก่อน แล้วค่อยโยนต่อให้ผู้เรียกนับจำนวนที่ล้ม
                _reportRepo.MarkFailed(deliveryId, Trim(ex.Message, 1000));
                throw;
            }
        }

        /// <summary>
        /// เนื้ออีเมลตั้งใจให้สั้น: บอกว่าเป็นเดือนไหน ขอบเขตไหน
        /// และภาพรวมสถานะพอให้ตัดสินใจได้ว่าต้องเปิดไฟล์ดูด่วนหรือไม่
        /// รายละเอียดทั้งหมดอยู่ใน PDF ที่แนบไป
        /// </summary>
        private static string BuildBody(KpiReportData data, ReportSubscription sub)
        {
            var sb = new StringBuilder();

            sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1b2430;\">");
            sb.Append("<p>เรียน ").Append(Encode(sub.DisplayName ?? sub.Email)).Append("</p>");

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
