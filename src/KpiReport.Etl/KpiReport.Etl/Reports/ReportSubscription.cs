using System;

namespace KpiReport.Etl.Reports
{
    /// <summary>1 แถวจาก meta.vw_ActiveReportSubscription</summary>
    public class ReportSubscription
    {
        public int SubscriptionId { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }

        /// <summary>null = ได้รายงานภาพรวมทั้งบริษัท + แยกรายแผนก</summary>
        public int? DepartmentId { get; set; }

        public string DepartmentName { get; set; }

        public bool IsCompanyWide
        {
            get { return !DepartmentId.HasValue; }
        }

        /// <summary>วันที่ของเดือนที่ผู้รับรายนี้ตั้งไว้ (1–31)</summary>
        public byte SendDayOfMonth { get; set; }

        /// <summary>ชั่วโมงที่ตั้งไว้ (0–23) ตามเวลาเครื่องที่รันงาน</summary>
        public byte SendHour { get; set; }

        /// <summary>ชื่อขอบเขตที่จะพิมพ์บนหัวรายงานและใช้เป็นคีย์กันส่งซ้ำ</summary>
        public string ScopeLabel
        {
            get { return IsCompanyWide ? "All Departments" : (DepartmentName ?? "#" + DepartmentId); }
        }

        /// <summary>
        /// เวลาที่ถึงกำหนดส่งของเดือนที่ระบุ
        ///
        /// วันที่เกินจำนวนวันของเดือนนั้นถูกร่นลงมาเป็นวันสุดท้าย
        /// ตั้ง 31 ในเดือนกุมภาพันธ์จึงหมายถึงวันที่ 28 หรือ 29
        /// ถ้าไม่ร่นให้ คนที่ตั้ง "สิ้นเดือน" จะไม่ได้รับรายงานในเดือนที่สั้นกว่าเลย
        /// </summary>
        public DateTime ScheduledTimeIn(int year, int month)
        {
            int daysInMonth = DateTime.DaysInMonth(year, month);
            int day = SendDayOfMonth < 1 ? 1 : (SendDayOfMonth > daysInMonth ? daysInMonth : SendDayOfMonth);
            int hour = SendHour > 23 ? 23 : SendHour;
            return new DateTime(year, month, day, hour, 0, 0);
        }

        /// <summary>
        /// ถึงกำหนดของเดือนปัจจุบันแล้วหรือยัง
        ///
        /// ใช้ "เลยเวลาที่กำหนดมาแล้วหรือยัง" ไม่ใช่ "ตรงกับชั่วโมงนี้พอดีไหม"
        /// ตั้งใจให้ทนต่อกรณีเครื่องปิดอยู่ตอนถึงเวลา หรืองานไม่ได้รัน
        /// พอกลับมารันรอบถัดไปก็ยังส่งให้ ไม่ตกรอบทั้งเดือน
        /// ส่วนการกันส่งซ้ำเป็นหน้าที่ของ meta.ReportDeliveryLog อีกชั้นหนึ่ง
        /// </summary>
        public bool IsDue(DateTime now)
        {
            return now >= ScheduledTimeIn(now.Year, now.Month);
        }

        public string ScheduleText
        {
            get { return "ทุกวันที่ " + SendDayOfMonth + " เวลา " + SendHour.ToString("00") + ":00"; }
        }
    }
}
