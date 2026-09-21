using System;
using System.Globalization;

namespace KpiReport.Web.Models
{
    /// <summary>
    /// 1 แถวจาก meta.vw_ReportDeliveryLog — ความพยายามส่งรายงาน 1 ฉบับ
    ///
    /// ไฟล์นี้ถูกผูกเข้า ETL ด้วย csproj Link เหมือน ReportDeliveryRepository
    /// ที่อ่านมันออกมา จึงตั้งใจไม่พึ่ง System.Web หรือ MVC
    /// </summary>
    public class ReportDeliveryLogRow
    {
        public long DeliveryId { get; set; }

        /// <summary>null = แถวเก่าที่ยังผูกกับผู้รับรายไหนไม่ได้ (อีเมลซ้ำ/ผู้รับถูกลบ)</summary>
        public int? SubscriptionId { get; set; }

        public int MonthKey { get; set; }

        /// <summary>คีย์กันส่งซ้ำ เช่น 'KPI_Monthly:ALL' หรือ 'KPI_Monthly_Manual:Purchasing'</summary>
        public string ReportName { get; set; }

        public string FileFormat { get; set; }
        public long? FileSizeBytes { get; set; }

        public string Email { get; set; }
        public string DisplayName { get; set; }

        /// <summary>ขอบเขตแผนก ณ เวลาที่ส่ง ไม่ใช่ขอบเขตปัจจุบันของผู้รับ</summary>
        public string ScopeLabel { get; set; }

        /// <summary>SCHEDULED = รอบอัตโนมัติ / MANUAL = มีคนกดส่ง</summary>
        public string TriggerType { get; set; }

        /// <summary>คนที่กดส่ง — null เมื่อเป็นรอบอัตโนมัติ</summary>
        public string TriggeredBy { get; set; }

        /// <summary>PENDING / SENT / FAILED</summary>
        public string Status { get; set; }

        public string ErrorMessage { get; set; }
        public int RetryCount { get; set; }

        /// <summary>เวลาที่เริ่มพยายามส่ง (เขียนก่อนยิงเมลเสมอ)</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>เวลาที่ส่งสำเร็จ — null เมื่อยัง PENDING หรือ FAILED</summary>
        public DateTime? SentAt { get; set; }

        public bool IsManual
        {
            get { return string.Equals(TriggerType, "MANUAL", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsSent { get { return Status == "SENT"; } }
        public bool IsFailed { get { return Status == "FAILED"; } }

        /// <summary>
        /// PENDING ที่ค้างเกิน 15 นาที = โปรเซสตายก่อนได้บันทึกผล
        /// ไม่ใช่ "กำลังส่ง" จริง ๆ จึงต้องแยกให้ผู้ดูแลเห็นว่าเป็นของค้าง
        /// </summary>
        public bool IsStalePending
        {
            get { return Status == "PENDING" && (DateTime.Now - CreatedAt).TotalMinutes > 15; }
        }

        /// <summary>202609 -> "Sep 2026"</summary>
        public string MonthLabel
        {
            get
            {
                if (MonthKey < 190001) return MonthKey.ToString(CultureInfo.InvariantCulture);

                int year = MonthKey / 100;
                int month = MonthKey % 100;
                if (month < 1 || month > 12) return MonthKey.ToString(CultureInfo.InvariantCulture);

                return new DateTime(year, month, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture);
            }
        }

        public string TriggerLabel
        {
            get { return IsManual ? "กดส่งเอง" : "ตามรอบ"; }
        }

        /// <summary>ที่มาแบบเต็ม — รอบอัตโนมัติไม่มีคนกด จึงบอกว่าเป็นของระบบ</summary>
        public string TriggerDetail
        {
            get
            {
                if (!IsManual) return "รอบอัตโนมัติ (ETL)";
                return string.IsNullOrEmpty(TriggeredBy) ? "กดส่งเอง" : "กดส่งโดย " + TriggeredBy;
            }
        }

        public string StatusLabel
        {
            get
            {
                if (IsSent) return "ส่งแล้ว";
                if (IsFailed) return "ล้มเหลว";
                return IsStalePending ? "ค้าง" : "กำลังส่ง";
            }
        }

        public string FileSizeLabel
        {
            get
            {
                if (FileSizeBytes == null || FileSizeBytes <= 0) return "-";
                if (FileSizeBytes < 1024) return FileSizeBytes + " B";

                double kb = FileSizeBytes.Value / 1024d;
                if (kb < 1024) return kb.ToString("0.#", CultureInfo.InvariantCulture) + " KB";

                return (kb / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            }
        }

        /// <summary>เวลาที่ควรแสดงในตาราง — เวลาที่ส่งสำเร็จถ้ามี ไม่มีก็เวลาที่เริ่มพยายาม</summary>
        public DateTime EventAt
        {
            get { return SentAt ?? CreatedAt; }
        }
    }
}
