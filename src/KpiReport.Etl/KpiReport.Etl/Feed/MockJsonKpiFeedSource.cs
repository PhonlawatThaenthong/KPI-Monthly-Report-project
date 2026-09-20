using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KpiReport.Etl.Infrastructure;
using KpiReport.Etl.Models;
using Newtonsoft.Json.Linq;

namespace KpiReport.Etl.Feed
{
    /// <summary>
    /// แหล่งข้อมูลจำลอง: อ่านไฟล์ JSON รายเดือน (KPI รายบุคคล) จากโฟลเดอร์ mock-data/kpi-feed
    ///
    /// รูปแบบไฟล์จงใจทำให้เหมือนสิ่งที่ REST API ของระบบต้นทางจะตอบกลับมา
    /// เพื่อให้วันเปลี่ยนไปต่อของจริงแล้วโค้ดฝั่ง transform ไม่ต้องแก้เลย
    ///
    ///   {
    ///     "source":      "HRIS-KPI-Service (mock)",
    ///     "level":       "EMPLOYEE",
    ///     "generatedAt": "2026-07-05T02:00:00+07:00",
    ///     "period":      "2026-06",
    ///     "records": [
    ///       { "employeeCode": "EMP-0001", "employeeName": "...",
    ///         "department": "HR", "metricCode": "EMP_TRAINING",
    ///         "target": 6.0, "actual": 7.5,
    ///         "status": "DONE", "completedDate": "2026-06-21" }
    ///     ]
    ///   }
    ///
    /// อ่านทุกค่าเป็น "ข้อความ" ตามที่เขียนมาในไฟล์ ไม่แปลงชนิดตรงนี้
    /// ค่าที่เพี้ยน (เช่น "N/A", "1,234") จึงไหลเข้า staging ได้ครบ
    /// แล้วไปถูกตรวจ/ตัดทิ้งพร้อมเหตุผลที่ meta.DataRejectLog
    /// </summary>
    public class MockJsonKpiFeedSource : IKpiFeedSource
    {
        private readonly string _folder;

        public MockJsonKpiFeedSource(string folder)
        {
            _folder = folder;
        }

        public string Description
        {
            get { return "MOCK JSON: " + _folder; }
        }

        public IList<KpiFeedBatch> Fetch(int? monthKey)
        {
            if (string.IsNullOrEmpty(_folder) || !Directory.Exists(_folder))
                throw new DirectoryNotFoundException(
                    "ไม่พบโฟลเดอร์ mock ของ KPI feed: '" +
                    (string.IsNullOrEmpty(_folder) ? "(ไม่ได้ตั้งค่า)" : Path.GetFullPath(_folder)) +
                    "' — แก้ App.config key 'KpiFeed:MockFolder' ให้เป็น path เต็ม");

            var batches = new List<KpiFeedBatch>();

            foreach (string file in Directory.GetFiles(_folder, "*.json").OrderBy(f => f))
            {
                // กรองด้วยชื่อไฟล์ก่อนเปิดอ่าน: EmpKpiFeed_202606.json
                if (monthKey.HasValue &&
                    Path.GetFileNameWithoutExtension(file).IndexOf(
                        monthKey.Value.ToString(), StringComparison.Ordinal) < 0)
                    continue;

                batches.Add(ReadFile(file));
            }

            return batches;
        }

        private static KpiFeedBatch ReadFile(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            var info = new FileInfo(filePath);

            var batch = new KpiFeedBatch
            {
                SourceName = fileName,
                DedupeKey = FileHashUtil.ComputeSha256(filePath),
                SizeBytes = info.Length,
                ModifiedAtUtc = info.LastWriteTimeUtc
            };

            JObject root = JObject.Parse(File.ReadAllText(filePath));
            string period = Text(root["period"]);

            // ยอมรับชื่อเดิม "metrics" ด้วย เผื่อไฟล์รุ่นก่อนยังค้างอยู่ในโฟลเดอร์
            var records = (root["records"] as JArray) ?? (root["metrics"] as JArray);
            if (records == null)
                throw new InvalidDataException(
                    "ไฟล์ " + fileName + " ไม่มี array 'records' — รูปแบบไม่ตรงกับที่ตกลงไว้");

            int line = 0;
            foreach (JToken m in records)
            {
                line++;
                batch.Rows.Add(new KpiFeedRow
                {
                    SourceName = fileName,
                    SourceLineNo = line,
                    // เดือนอยู่ที่หัวไฟล์ แต่ยอมให้แต่ละแถวเขียนทับได้
                    // เผื่อต้นทางส่งค่าย้อนหลังมาปนในไฟล์เดียว
                    MonthText = Text(m["period"]) ?? period,
                    EmployeeCodeText = Text(m["employeeCode"]),
                    EmployeeNameText = Text(m["employeeName"]),
                    DepartmentText = Text(m["department"]),
                    KpiCodeText = Text(m["metricCode"]),
                    TargetValueText = Text(m["target"]),
                    ActualValueText = Text(m["actual"]),
                    StatusText = Text(m["status"]),
                    CompletedDateText = Text(m["completedDate"])
                });
            }

            return batch;
        }

        /// <summary>
        /// อ่าน JSON token เป็นข้อความดิบ — null/หายไป -> null, ที่เหลือ -> ToString()
        /// ตัวเลขที่เขียนเป็น string ในไฟล์ ("1,234") จึงผ่านมาได้เหมือนกัน
        /// </summary>
        private static string Text(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                return null;

            return token.Type == JTokenType.String
                ? token.Value<string>()
                : token.ToString();
        }
    }
}
