using System;
using System.Configuration;
using System.Linq;
using KpiReport.Etl.Db;
using KpiReport.Etl.Feed;
using KpiReport.Etl.Reports;

namespace KpiReport.Etl
{
    /// <summary>
    /// จุดเข้าโปรแกรม รับคำสั่งผ่าน command line argument เดียว
    ///
    /// ระบบนี้ไม่คำนวณ KPI เอง — ดึงค่าที่ระบบต้นทางคำนวณไว้แล้วมาเก็บ
    /// หน่วยข้อมูลเป็น KPI รายบุคคล แล้วสรุปขึ้นเป็นระดับแผนกในฐานข้อมูล
    /// ตอนนี้ยังต่อของจริงไม่ได้ จึงอ่านจากไฟล์ JSON จำลองใน mock-data/kpi-feed
    /// สลับไปใช้ของจริงได้ที่ App.config key 'KpiFeed:Provider'
    ///
    /// การใช้งาน (จาก Task Scheduler หรือมือ):
    ///   KpiReport.Etl.exe run-all           ดึงค่า KPI ทุกเดือนที่ต้นทางมี
    ///   KpiReport.Etl.exe kpi-feed          เท่ากับ run-all
    ///   KpiReport.Etl.exe kpi-feed 202606   ดึงเฉพาะเดือนที่ระบุ
    ///   KpiReport.Etl.exe refresh-derived   คำนวณสี/ค่าเดือนก่อนใหม่ทั้งหมด
    ///   KpiReport.Etl.exe rollup [yyyyMM]   สรุป KPI รายบุคคลขึ้นเป็นระดับแผนกใหม่
    ///
    ///   KpiReport.Etl.exe send-report               ส่งรายงานเดือนล่าสุดทางอีเมล
    ///   KpiReport.Etl.exe send-report 202606        ส่งรายงานเดือนที่ระบุ
    ///   KpiReport.Etl.exe send-report --dry-run     ลองดูว่าจะส่งอะไรให้ใคร ไม่ส่งจริง
    ///   KpiReport.Etl.exe send-report 202606 --force ส่งซ้ำแม้เคยส่งสำเร็จไปแล้ว
    ///   KpiReport.Etl.exe send-report --ignore-schedule  ส่งทันทีโดยไม่ดูวัน/เวลาที่ตั้งไว้
    ///
    /// การตั้ง Task Scheduler สำหรับ send-report:
    ///   ตั้งให้รัน "ทุกชั่วโมง" คำสั่งเดียวพอ ไม่ต้องตั้งรายเดือน
    ///   เพราะวัน/เวลาส่งของแต่ละคนเก็บอยู่ในฐานข้อมูล (แก้ได้จากหน้าเว็บ)
    ///   โปรแกรมจะเป็นคนตัดสินเองว่ารอบนี้ถึงกำหนดของใครแล้วบ้าง
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string command = args.Length > 0 ? args[0].ToLowerInvariant() : "run-all";
            string triggeredBy = Environment.UserName + "@" + Environment.MachineName;

            SqlDb db;
            try
            {
                string connStr = ConfigurationManager
                    .ConnectionStrings["KpiDb"].ConnectionString;
                db = new SqlDb(connStr);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ไม่พบ connection string 'KpiDb' ใน App.config: " + ex.Message);
                return 1;
            }

            try
            {
                switch (command)
                {
                    case "run-all":
                    case "kpi-feed":
                        return RunKpiFeed(db, triggeredBy, ParseMonthArg(args));

                    case "rollup":
                        db.RollupKpiToDepartment(ParseMonthArg(args));
                        Console.WriteLine(">> สรุป KPI รายบุคคลขึ้นระดับแผนกเรียบร้อย");
                        break;

                    case "refresh-derived":
                        db.RefreshKpiDerived(ParseMonthArg(args));
                        Console.WriteLine(">> คำนวณค่าเดือนก่อน/สถานะใหม่เรียบร้อย");
                        break;

                    case "send-report":
                        return RunSendReport(args);

                    default:
                        PrintUsage();
                        return 1;
                }

                Console.WriteLine(">> เสร็จสมบูรณ์");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FATAL ERROR: " + ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("คำสั่งที่ใช้ได้: run-all | kpi-feed [yyyyMM] | rollup [yyyyMM] | refresh-derived [yyyyMM]");
            Console.WriteLine("                send-report [yyyyMM] [--dry-run] [--force] [--ignore-schedule]");
        }

        /// <summary>หา argument ที่เป็นเลขเดือน yyyyMM ถ้าไม่มีคืน null = ทุกเดือน</summary>
        private static int? ParseMonthArg(string[] args)
        {
            foreach (string arg in args.Skip(1))
            {
                int parsed;
                if (int.TryParse(arg, out parsed) && parsed >= 190001 && parsed <= 299912)
                    return parsed;
            }
            return null;
        }

        // =========================================================
        // SEND-REPORT — ส่งรายงาน KPI รายเดือนเป็น PDF ทางอีเมล
        //
        // คืน exit code = จำนวนฉบับที่ล้มเหลว (0 = สำเร็จหมด)
        // ตั้ง Task Scheduler ให้แจ้งเตือนเมื่อ exit code ไม่ใช่ 0 ได้เลย
        // =========================================================
        private static int RunSendReport(string[] args)
        {
            Console.WriteLine("== ส่งรายงาน KPI รายเดือนทางอีเมล ==");

            bool dryRun = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
            bool force = args.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase));
            bool ignoreSchedule = args.Any(a =>
                a.Equals("--ignore-schedule", StringComparison.OrdinalIgnoreCase)
                || a.Equals("--all", StringComparison.OrdinalIgnoreCase));

            int? monthKey = null;
            foreach (string arg in args.Skip(1))
            {
                int parsed;
                if (int.TryParse(arg, out parsed))
                {
                    monthKey = parsed;
                    break;
                }
            }

            string connStr = ConfigurationManager.ConnectionStrings["KpiDb"].ConnectionString;

            try
            {
                var job = new MonthlyReportJob(connStr);
                int failed = job.Run(monthKey, dryRun, force, ignoreSchedule);

                if (failed > 0)
                {
                    Console.Error.WriteLine(">> มี " + failed + " ฉบับที่ส่งไม่สำเร็จ ดูรายละเอียดที่ meta.ReportDeliveryLog");
                    return failed;
                }

                Console.WriteLine(">> เสร็จสมบูรณ์");
                return 0;
            }
            catch (System.Configuration.ConfigurationErrorsException ex)
            {
                // แยกกรณีตั้งค่าไม่ครบออกมา เพราะเป็นความผิดพลาดที่แก้ได้ทันที
                // ไม่ควรทำให้ดูเหมือนระบบพัง
                Console.Error.WriteLine("   [ตั้งค่าไม่ครบ] " + ex.Message);
                Console.Error.WriteLine("   ตรวจ App.config: appSettings 'Report:FromAddress' และ 'Smtp.*'"
                    + " รวมถึง environment variable KPI_SMTP_PASSWORD (ถ้า Smtp.DeliveryMethod=Network)");
                return 1;
            }
        }

        // =========================================================
        // KPI FEED — ดึงค่า KPI ที่ระบบต้นทางคำนวณไว้แล้ว
        //
        // คืน exit code 0 = สำเร็จ, 1 = ดึงไม่ได้/ไม่มีข้อมูล
        // =========================================================
        private static int RunKpiFeed(SqlDb db, string triggeredBy, int? monthKey)
        {
            IKpiFeedSource source;
            try
            {
                source = KpiFeedSourceFactory.Create();
            }
            catch (ConfigurationErrorsException ex)
            {
                Console.Error.WriteLine("   [ตั้งค่าไม่ครบ] " + ex.Message);
                return 1;
            }

            Console.WriteLine("== ดึงค่า KPI จากระบบต้นทาง ==");
            Console.WriteLine("   แหล่งข้อมูล: " + source.Description);
            Console.WriteLine("   ขอบเขต: " + (monthKey.HasValue ? monthKey.Value.ToString() : "ทุกเดือนที่มี"));

            System.Collections.Generic.IList<KpiFeedBatch> batches;
            try
            {
                batches = source.Fetch(monthKey);
            }
            catch (NotImplementedException ex)
            {
                // ยังต่อ API จริงไม่ได้ — เป็นสถานะที่รู้อยู่แล้ว ไม่ใช่ระบบพัง
                Console.Error.WriteLine("   [ยังเชื่อมต่อไม่ได้] " + ex.Message);
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("   [ดึงข้อมูลไม่สำเร็จ] " + ex.Message);
                return 1;
            }

            if (batches.Count == 0)
            {
                Console.WriteLine("   ต้นทางไม่มีข้อมูลตามเงื่อนไขที่ขอ");
                return 1;
            }

            long runId = db.EtlRunStart("ETL_KpiFeed", monthKey, triggeredBy);
            int totalRead = 0, written = 0, rejected = 0;
            int loaded = 0, skipped = 0;

            try
            {
                foreach (var batch in batches)
                {
                    // ชุดข้อมูลเดิมเป๊ะ ๆ ไม่ต้องโหลดซ้ำ (ลายนิ้วมือ SHA-256)
                    if (!string.IsNullOrEmpty(batch.DedupeKey) && db.FileAlreadyLoaded(batch.DedupeKey))
                    {
                        skipped++;
                        continue;
                    }

                    db.BulkInsertKpiEmployeeFeedRaw(runId, batch.Rows);

                    if (!string.IsNullOrEmpty(batch.DedupeKey))
                        db.RecordFileLoad(
                            runId, batch.SourceName, batch.DedupeKey,
                            batch.SizeBytes ?? 0,
                            batch.ModifiedAtUtc ?? DateTime.UtcNow,
                            batch.Rows.Count);

                    totalRead += batch.Rows.Count;
                    loaded++;
                    Console.WriteLine("   [load] " + batch.SourceName + " -> " + batch.Rows.Count + " รายการ KPI รายบุคคล");
                }

                Console.WriteLine("   ชุดใหม่ " + loaded + " | ข้าม (เคยโหลดแล้ว) " + skipped);

                db.EtlStepLog(runId, 1, "Extract_KpiFeed", source.Description,
                    "SUCCESS", totalRead, totalRead, null);

                if (totalRead == 0)
                {
                    db.EtlRunFinish(runId, "SUCCESS", 0, 0, 0, null);
                    Console.WriteLine("   ไม่มีข้อมูลใหม่ ไม่ต้องทำอะไรต่อ");
                    return 0;
                }

                var result = db.TransformKpiEmployeeFeed(runId);
                written = result.written;
                rejected = result.rejected;

                db.EtlStepLog(runId, 2, "Transform_KpiEmployeeFeed", "stg.KpiEmployeeFeedRaw",
                    "SUCCESS", totalRead, written, rejected);

                // สรุปขึ้นเป็นระดับแผนกทันที ไม่ปล่อยให้ค้างเป็นงานที่ต้องสั่งเอง
                // ไม่งั้นหน้า Dashboard จะยังแสดงตัวเลขเดือนก่อนอยู่ทั้งที่โหลดใหม่แล้ว
                db.RollupKpiToDepartment(monthKey);

                db.EtlStepLog(runId, 3, "Rollup_KpiEmployeeToDept", "core.FactKpiEmployeeMonthly",
                    "SUCCESS", written, written, 0);

                db.EtlRunFinish(runId, "SUCCESS", totalRead, written, rejected, null);

                Console.WriteLine("   RunId " + runId + " | อ่าน " + totalRead
                    + " | บันทึก " + written + " | ตัดออก " + rejected);

                if (rejected > 0)
                    Console.WriteLine("   มีแถวที่รับไม่ได้ ดูเหตุผลที่ meta.DataRejectLog (RunId " + runId + ")");

                return 0;
            }
            catch (Exception ex)
            {
                db.EtlRunFinish(runId, "FAILED", totalRead, written, rejected, ex.Message);
                throw;
            }
        }
    }
}
