using System;
using System.Configuration;
using System.IO;
using System.Linq;
using KpiReport.Etl.Db;
using KpiReport.Etl.Infrastructure;
using KpiReport.Etl.Sources;

namespace KpiReport.Etl
{
    /// <summary>
    /// จุดเข้าโปรแกรม รับคำสั่งผ่าน command line argument เดียว
    ///
    /// การใช้งาน (จาก Task Scheduler หรือมือ):
    ///   KpiReport.Etl.exe run-all         รันทุกแหล่ง + คำนวณ KPI ทุกเดือน
    ///   KpiReport.Etl.exe production      ดึง+แปลงเฉพาะข้อมูลการผลิต (ERP)
    ///   KpiReport.Etl.exe downtime        โหลด+แปลงเฉพาะ CSV เครื่องหยุด
    ///   KpiReport.Etl.exe cost            โหลด+แปลงเฉพาะ Excel ต้นทุน
    ///   KpiReport.Etl.exe kpi 202601      คำนวณ KPI เฉพาะเดือนที่ระบุ
    ///   KpiReport.Etl.exe kpi-all         คำนวณ KPI ทุกเดือนที่มีข้อมูล
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
                        RunProduction(db, triggeredBy);
                        RunDowntime(db, triggeredBy);
                        RunCost(db, triggeredBy);
                        Console.WriteLine(">> คำนวณ KPI ทุกเดือน ...");
                        db.RunKpiAllMonths(triggeredBy);
                        break;

                    case "production":
                        RunProduction(db, triggeredBy);
                        break;

                    case "downtime":
                        RunDowntime(db, triggeredBy);
                        break;

                    case "cost":
                        RunCost(db, triggeredBy);
                        break;

                    case "kpi":
                        if (args.Length < 2 || !int.TryParse(args[1], out int monthKey))
                        {
                            Console.Error.WriteLine("ใช้งาน: KpiReport.Etl.exe kpi <yyyyMM>  เช่น kpi 202601");
                            return 1;
                        }
                        db.RunKpiMonthly(monthKey, triggeredBy);
                        Console.WriteLine($">> คำนวณ KPI เดือน {monthKey} เสร็จแล้ว");
                        break;

                    case "kpi-all":
                        db.RunKpiAllMonths(triggeredBy);
                        Console.WriteLine(">> คำนวณ KPI ทุกเดือนเสร็จแล้ว");
                        break;

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
            Console.WriteLine("คำสั่งที่ใช้ได้: run-all | production | downtime | cost | kpi <yyyyMM> | kpi-all");
        }

        // =========================================================
        // PRODUCTION - extract + transform ทำใน SQL ทั้งหมด
        // C# แค่เรียก proc ตัวเดียว
        // =========================================================
        private static void RunProduction(SqlDb db, string triggeredBy)
        {
            Console.WriteLine("== Production (ERP) ==");
            db.RunEtlProduction(triggeredBy);
            Console.WriteLine("   เสร็จ (ดูรายละเอียดที่ meta.EtlRunLog JobName='ETL_Production')");
        }

        // =========================================================
        // DOWNTIME - อ่านไฟล์ CSV ทีละไฟล์ กันโหลดซ้ำด้วย hash
        // =========================================================
        private static void RunDowntime(SqlDb db, string triggeredBy)
        {
            Console.WriteLine("== Downtime (CSV) ==");

            string folder = ConfigurationManager.AppSettings["DowntimeFolder"];
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                Console.Error.WriteLine($"   ไม่พบโฟลเดอร์ '{folder}' ตรวจ App.config key 'DowntimeFolder'");
                return;
            }

            var files = Directory.GetFiles(folder, "*.csv").OrderBy(f => f).ToList();
            if (files.Count == 0)
            {
                Console.WriteLine("   ไม่พบไฟล์ CSV ในโฟลเดอร์");
                return;
            }

            long runId = db.EtlRunStart("ETL_Downtime", null, triggeredBy);
            int totalRead = 0, written = 0, rejected = 0;
            int filesLoaded = 0, filesSkipped = 0;

            try
            {
                foreach (var file in files)
                {
                    string hash = FileHashUtil.ComputeSha256(file);

                    if (db.FileAlreadyLoaded(hash))
                    {
                        filesSkipped++;
                        continue;
                    }

                    var rows = DowntimeCsvReader.Read(file);
                    db.BulkInsertDowntimeRaw(runId, rows);
                    db.RecordFileLoad(
                        runId, Path.GetFileName(file), hash,
                        new FileInfo(file).Length, File.GetLastWriteTimeUtc(file), rows.Count);

                    totalRead += rows.Count;
                    filesLoaded++;
                    Console.WriteLine($"   [load] {Path.GetFileName(file)} -> {rows.Count} แถว");
                }

                Console.WriteLine($"   ไฟล์ใหม่ {filesLoaded} | ข้าม (เคยโหลดแล้ว) {filesSkipped}");

                db.EtlStepLog(runId, 1, "Extract_Downtime", folder, "SUCCESS", totalRead, totalRead, null);

                var result = db.TransformDowntime(runId);
                written = result.written;
                rejected = result.rejected;

                db.EtlStepLog(runId, 2, "Transform_Downtime", "stg.DowntimeRaw",
                    "SUCCESS", totalRead, written, rejected);

                db.EtlRunFinish(runId, "SUCCESS", totalRead, written, rejected, null);

                Console.WriteLine($"   RunId {runId} | อ่าน {totalRead} | บันทึก {written} | ตัดออก {rejected}");
            }
            catch (Exception ex)
            {
                db.EtlRunFinish(runId, "FAILED", totalRead, written, rejected, ex.Message);
                throw;
            }
        }

        // =========================================================
        // COST - อ่าน Excel ไฟล์เดียว หลาย sheet
        // =========================================================
        private static void RunCost(SqlDb db, string triggeredBy)
        {
            Console.WriteLine("== Cost (Excel) ==");

            string path = ConfigurationManager.AppSettings["CostExcelPath"];
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Console.Error.WriteLine($"   ไม่พบไฟล์ '{path}' ตรวจ App.config key 'CostExcelPath'");
                return;
            }

            string hash = FileHashUtil.ComputeSha256(path);
            if (db.FileAlreadyLoaded(hash))
            {
                // ไฟล์นี้ (ทุก sheet รวมกัน) เคยโหลดไปแล้วทั้งไฟล์
                // เพราะ generator สร้างไฟล์ใหม่ทับทุกครั้ง เนื้อหาเปลี่ยน = hash เปลี่ยน = โหลดใหม่อัตโนมัติ
                Console.WriteLine("   ไฟล์นี้เคยโหลดไปแล้ว (เนื้อหาไม่เปลี่ยน) ข้าม");
                return;
            }

            long runId = db.EtlRunStart("ETL_Cost", null, triggeredBy);
            int totalRead = 0, written = 0, rejected = 0;

            try
            {
                var rows = CostExcelReader.Read(path);
                db.BulkInsertCostRaw(runId, rows);
                db.RecordFileLoad(
                    runId, Path.GetFileName(path), hash,
                    new FileInfo(path).Length, File.GetLastWriteTimeUtc(path), rows.Count);

                totalRead = rows.Count;
                Console.WriteLine($"   [load] {Path.GetFileName(path)} -> {rows.Count} แถว (ทุก sheet รวมกัน)");

                db.EtlStepLog(runId, 1, "Extract_Cost", path, "SUCCESS", totalRead, totalRead, null);

                var result = db.TransformCost(runId);
                written = result.written;
                rejected = result.rejected;

                db.EtlStepLog(runId, 2, "Transform_Cost", "stg.CostRaw",
                    "SUCCESS", totalRead, written, rejected);

                db.EtlRunFinish(runId, "SUCCESS", totalRead, written, rejected, null);

                Console.WriteLine($"   RunId {runId} | อ่าน {totalRead} | บันทึก {written} | ตัดออก {rejected}");
            }
            catch (Exception ex)
            {
                db.EtlRunFinish(runId, "FAILED", totalRead, written, rejected, ex.Message);
                throw;
            }
        }
    }
}
