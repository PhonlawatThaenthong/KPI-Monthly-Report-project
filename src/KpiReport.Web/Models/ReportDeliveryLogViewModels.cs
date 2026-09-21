using System.Collections.Generic;
using System.Linq;

namespace KpiReport.Web.Models
{
    /// <summary>ตัวนับที่ใช้ทั้งหน้า log ต่อผู้รับและหน้ารวมทุกการส่ง</summary>
    public class DeliveryLogStats
    {
        public int Total { get; set; }
        public int Sent { get; set; }
        public int Failed { get; set; }
        public int Pending { get; set; }
        public int Manual { get; set; }
        public int Scheduled { get; set; }

        public static DeliveryLogStats From(IEnumerable<ReportDeliveryLogRow> rows)
        {
            var list = rows as IList<ReportDeliveryLogRow> ?? rows.ToList();

            return new DeliveryLogStats
            {
                Total = list.Count,
                Sent = list.Count(r => r.IsSent),
                Failed = list.Count(r => r.IsFailed),
                Pending = list.Count(r => !r.IsSent && !r.IsFailed),
                Manual = list.Count(r => r.IsManual),
                Scheduled = list.Count(r => !r.IsManual)
            };
        }
    }

    /// <summary>หน้า log ของผู้รับ 1 ราย</summary>
    public class ReportDeliveryLogViewModel
    {
        /// <summary>ผู้รับที่กำลังดู — ใช้หัวเรื่องและปุ่มย้อนกลับ</summary>
        public ReportSubscriptionRow Subscription { get; set; }

        public List<ReportDeliveryLogRow> Rows { get; set; } = new List<ReportDeliveryLogRow>();

        public DeliveryLogStats Stats { get; set; } = new DeliveryLogStats();

        /// <summary>จำนวนแถวสูงสุดที่ดึงมา ใช้เตือนเมื่อ log ถูกตัด</summary>
        public int Take { get; set; }

        public bool Truncated { get { return Rows.Count >= Take; } }

        /// <summary>ฉบับล่าสุดที่ส่งสำเร็จ — คำถามแรกที่ผู้ดูแลมักถาม</summary>
        public ReportDeliveryLogRow LastSent
        {
            get { return Rows.FirstOrDefault(r => r.IsSent); }
        }
    }

    /// <summary>หน้ารวมทุกการส่ง พร้อมตัวกรอง</summary>
    public class DeliveryLogSearchViewModel
    {
        public List<ReportDeliveryLogRow> Rows { get; set; } = new List<ReportDeliveryLogRow>();

        public DeliveryLogStats Stats { get; set; } = new DeliveryLogStats();

        /// <summary>เดือนที่มี log อยู่จริง</summary>
        public List<int> AvailableMonths { get; set; } = new List<int>();

        public int? MonthKey { get; set; }

        /// <summary>SENT / FAILED / PENDING — null = ทุกสถานะ</summary>
        public string Status { get; set; }

        /// <summary>SCHEDULED / MANUAL — null = ทั้งสองแบบ</summary>
        public string TriggerType { get; set; }

        public int Take { get; set; }

        public bool Truncated { get { return Rows.Count >= Take; } }

        /// <summary>true = ผู้ใช้คนนี้เห็นได้เฉพาะผู้รับในแผนกที่ตัวเองดูแล</summary>
        public bool IsScoped { get; set; }
    }
}
