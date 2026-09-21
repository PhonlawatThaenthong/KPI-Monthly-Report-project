using System;
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
    /// ตัวสร้าง PDF ตัวอ่านข้อมูล และตัวส่งเมล (ReportMailer) ใช้ไฟล์ชุด
    /// เดียวกับเว็บ (ผูกด้วย Link ใน .csproj ไม่ได้ copy) ตัวเลขในอีเมลจึง
    /// ตรงกับหน้าจอเสมอ และปุ่ม "ส่งเดี๋ยวนี้" ในหน้าผู้รับรายงานก็ได้ไฟล์
    /// หน้าตาเดียวกับรอบอัตโนมัติ แก้สูตรที่เดียวมีผลทุกทาง
    /// </summary>
    public class MonthlyReportJob
    {
        private readonly ReportRepository _reportRepo;
        private readonly ReportDeliveryRepository _deliveryRepo;
        private readonly ReportMailer _mailer;

        public MonthlyReportJob(string connectionString)
        {
            _reportRepo = new ReportRepository(connectionString);
            _deliveryRepo = new ReportDeliveryRepository(connectionString);
            _mailer = new ReportMailer(connectionString);
        }

        /// <summary>
        /// คืนจำนวนฉบับที่ล้มเหลว (0 = สำเร็จหมด) เพื่อให้ Program คืน exit code
        /// ที่ Task Scheduler เอาไปตั้งแจ้งเตือนได้
        /// </summary>
        public int Run(int? requestedMonthKey, bool dryRun, bool force, bool ignoreSchedule)
        {
            DateTime now = DateTime.Now;

            int? monthKey = requestedMonthKey ?? _deliveryRepo.GetLatestMonthKey();

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

            Console.WriteLine("   เวลาปัจจุบัน: " + now.ToString("d MMM yyyy HH:mm"));
            Console.WriteLine("   เดือนรายงาน: " + monthKey.Value);
            Console.WriteLine("   ผู้รับ      : " + subscriptions.Count + " ราย");
            Console.WriteLine("   ตารางเวลา  : " + (ignoreSchedule ? "ข้าม (ส่งทุกรายที่ยังไม่เคยส่ง)" : "ตามที่ตั้งไว้ต่อราย"));
            Console.WriteLine("   ช่องทางส่ง : " + (dryRun ? "DRY RUN (ไม่ส่งจริง)" : KpiReport.Shared.Mail.SmtpMailSender.DescribeDeliveryMode()));
            Console.WriteLine();

            int failed = 0;
            int skipped = 0;
            int sent = 0;
            int notDue = 0;

            foreach (var sub in subscriptions)
            {
                var recipient = ToRecipient(sub);
                string reportName = ReportMailer.ReportNameFor(recipient);

                // ยังไม่ถึงวัน/เวลาที่ผู้รับรายนี้ตั้งไว้ในเดือนนี้
                // งานถูกเรียกทุกชั่วโมงจาก Task Scheduler ส่วนใหญ่จึงจะตกที่นี่
                if (!ignoreSchedule && !dryRun && !sub.IsDue(now))
                {
                    Console.WriteLine("   . ยังไม่ถึงกำหนด " + sub.Email + " (" + sub.ScheduleText
                                      + ") — รอบนี้ " + sub.ScheduledTimeIn(now.Year, now.Month).ToString("d MMM HH:mm"));
                    notDue++;
                    continue;
                }

                // เช็คเฉพาะ log ของรอบอัตโนมัติ ปุ่ม "ส่งเดี๋ยวนี้" ในเว็บเขียน log
                // ด้วยคีย์ KPI_Monthly_Manual: ต่างหาก การกดส่งมือจึงไม่ทำให้
                // ผู้รับรายนั้นหลุดรอบประจำเดือนไป
                if (!force && !dryRun && _reportRepo.AlreadySent(monthKey.Value, reportName, sub.Email))
                {
                    Console.WriteLine("   - ข้าม " + sub.Email + " (" + sub.ScopeLabel + ") — เคยส่งเดือนนี้ไปแล้ว");
                    skipped++;
                    continue;
                }

                try
                {
                    var result = _mailer.Send(recipient, monthKey.Value, reportName,
                                              "Automated monthly delivery", dryRun,
                                              subscriptionId: sub.SubscriptionId,
                                              triggerType: "SCHEDULED");

                    if (!result.HasData)
                    {
                        Console.WriteLine("   - ข้าม " + sub.Email + " (" + sub.ScopeLabel + ") — ไม่มีข้อมูล KPI ของเดือนนี้");
                        skipped++;
                        continue;
                    }

                    if (dryRun)
                    {
                        string due = sub.IsDue(now) ? "ถึงกำหนดแล้ว" : "ยังไม่ถึงกำหนด";

                        Console.WriteLine("   . (dry run) " + sub.Email + " (" + sub.ScopeLabel + ") — "
                                          + result.KpiCount + " KPI, PDF " + result.PdfBytes / 1024 + " KB"
                                          + " · " + sub.ScheduleText + " · " + due);
                    }
                    else
                    {
                        Console.WriteLine("   + ส่งแล้ว " + sub.Email + " (" + sub.ScopeLabel + ") — " + result.FileName);
                    }

                    sent++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("   ! ล้มเหลว " + sub.Email + " : " + ex.Message);
                    failed++;
                }
            }

            Console.WriteLine();
            Console.WriteLine("   สรุป: ส่ง " + sent + " · ยังไม่ถึงกำหนด " + notDue
                              + " · ข้าม " + skipped + " · ล้มเหลว " + failed);
            return failed;
        }

        private static ReportRecipient ToRecipient(ReportSubscription sub)
        {
            return new ReportRecipient
            {
                Email = sub.Email,
                DisplayName = sub.DisplayName,
                DepartmentIds = sub.DepartmentIds,
                DepartmentNames = sub.DepartmentNames,
                DepartmentCount = sub.DepartmentCount
            };
        }
    }
}
