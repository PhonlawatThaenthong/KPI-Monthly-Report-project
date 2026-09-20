using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace KpiReport.Web.Models
{
    /// <summary>1 แถวจาก meta.vw_ReportSubscriptionAdmin</summary>
    public class ReportSubscriptionRow
    {
        public int SubscriptionId { get; set; }
        public string UserId { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }

        /// <summary>
        /// ขอบเขตของรายงานฉบับนี้ — รายการ DepartmentId คั่นด้วย comma ('3,5,8')
        /// null/ว่าง = ได้รายงานภาพรวมทั้งบริษัท + แยกรายแผนก
        ///
        /// ผู้รับหนึ่งคนมีแถวเดียว แต่เลือกได้หลายแผนกในฉบับเดียว
        /// (ของเดิมต้องสร้างหลายแถว = ได้อีเมลหลายฉบับต่อเดือน)
        /// </summary>
        public string DepartmentIds { get; set; }
        public string DepartmentNames { get; set; }
        public int DepartmentCount { get; set; }

        /// <summary>รายการแผนกที่เลือกไว้ ใช้ติ๊กช่องในฟอร์มแก้ขอบเขต</summary>
        public List<int> SelectedDepartmentIds
        {
            get
            {
                var list = new List<int>();
                if (string.IsNullOrEmpty(DepartmentIds)) return list;

                foreach (string part in DepartmentIds.Split(','))
                {
                    int id;
                    if (int.TryParse(part.Trim(), out id)) list.Add(id);
                }
                return list;
            }
        }

        public bool IsActive { get; set; }

        /// <summary>วันที่ของเดือนที่จะส่ง (1–31)</summary>
        public byte SendDayOfMonth { get; set; }

        /// <summary>ชั่วโมงที่จะส่ง (0–23) ตามเวลาเครื่องที่รันงาน</summary>
        public byte SendHour { get; set; }

        /// <summary>true = ผูกกับบัญชีในระบบ (อีเมลตามบัญชีเสมอ)</summary>
        public bool IsLinkedToUser { get; set; }

        /// <summary>true = เคยผูกกับบัญชี แต่บัญชีนั้นถูกลบไปแล้ว</summary>
        public bool LinkedUserMissing { get; set; }

        /// <summary>true = บัญชีที่ผูกไว้ถูกปิดใช้งาน จึงหยุดส่งอัตโนมัติ</summary>
        public bool LinkedUserDisabled { get; set; }

        public string ScopeLabel
        {
            get
            {
                if (DepartmentCount == 0) return "ทุกแผนก";
                return DepartmentNames ?? DepartmentIds;
            }
        }

        /// <summary>สรุปว่ารอบเดือนหน้าจะได้รับจริงหรือไม่ — ต้องผ่านทุกเงื่อนไข</summary>
        public bool WillReceive
        {
            get { return IsActive && !LinkedUserMissing && !LinkedUserDisabled; }
        }

        /// <summary>เช่น "ทุกวันที่ 3 เวลา 08:00"</summary>
        public string ScheduleText
        {
            get { return "ทุกวันที่ " + SendDayOfMonth + " เวลา " + SendHour.ToString("00") + ":00"; }
        }

        /// <summary>
        /// รอบส่งถัดไปโดยประมาณ
        ///
        /// วันที่เกินจำนวนวันของเดือนนั้นจะถูกร่นลงมาเป็นวันสุดท้าย
        /// (ตั้ง 31 ในเดือนกุมภาพันธ์ = วันที่ 28 หรือ 29)
        /// ไม่งั้นผู้ที่ตั้ง "สิ้นเดือน" จะไม่ได้รับรายงานในเดือนที่สั้นกว่า
        /// </summary>
        public DateTime NextSendAt
        {
            get
            {
                DateTime now = DateTime.Now;
                DateTime thisMonth = Occurrence(now.Year, now.Month);

                if (now < thisMonth) return thisMonth;

                DateTime next = now.AddMonths(1);
                return Occurrence(next.Year, next.Month);
            }
        }

        private DateTime Occurrence(int year, int month)
        {
            int daysInMonth = DateTime.DaysInMonth(year, month);
            int day = SendDayOfMonth < 1 ? 1 : (SendDayOfMonth > daysInMonth ? daysInMonth : SendDayOfMonth);
            int hour = SendHour > 23 ? 0 : SendHour;
            return new DateTime(year, month, day, hour, 0, 0);
        }

        /// <summary>เหตุผลที่ไม่ได้รับ ให้ผู้ดูแลเห็นว่าเงียบเพราะอะไร</summary>
        public string SilentReason
        {
            get
            {
                if (LinkedUserMissing) return "บัญชีที่ผูกไว้ถูกลบแล้ว";
                if (LinkedUserDisabled) return "บัญชีที่ผูกไว้ถูกปิดใช้งาน";
                if (!IsActive) return "ปิดรับรายงานไว้";
                return null;
            }
        }
    }

    public class ReportSubscriptionListViewModel
    {
        public List<ReportSubscriptionRow> Rows { get; set; } = new List<ReportSubscriptionRow>();

        /// <summary>บัญชีในระบบที่ยังไม่ได้เป็นผู้รับ ใช้เติม dropdown</summary>
        public List<UserOption> AvailableUsers { get; set; } = new List<UserOption>();

        public List<DepartmentOption> Departments { get; set; } = new List<DepartmentOption>();

        public ReportSubscriptionCreateViewModel NewSubscription { get; set; }
            = new ReportSubscriptionCreateViewModel();

        public int CountReceiving { get; set; }
        public int CountSilent { get; set; }
    }

    /// <summary>ตัวเลือกบัญชีผู้ใช้ใน dropdown</summary>
    public class UserOption
    {
        public string UserId { get; set; }
        public string Email { get; set; }

        /// <summary>แผนกที่บัญชีนี้ผูกอยู่ ใช้เลือกขอบเขตให้อัตโนมัติ</summary>
        public int? DepartmentId { get; set; }
        public string Label { get; set; }
    }

    public class ReportSubscriptionCreateViewModel
    {
        /// <summary>"User" = เลือกจากบัญชีในระบบ, "External" = พิมพ์อีเมลเอง</summary>
        public string SourceType { get; set; } = "User";

        /// <summary>ใช้เมื่อ SourceType = User</summary>
        public string UserId { get; set; }

        /// <summary>ใช้เมื่อ SourceType = External</summary>
        [EmailAddress(ErrorMessage = "รูปแบบอีเมลไม่ถูกต้อง")]
        public string Email { get; set; }

        public string DisplayName { get; set; }

        /// <summary>ไม่เลือกเลย = ทุกแผนก</summary>
        public int[] DepartmentIds { get; set; }

        /// <summary>ค่าเริ่มต้นวันที่ 3 : เผื่อเวลาให้ ETL ปิดยอดเดือนก่อนหน้าเสร็จก่อน</summary>
        public byte SendDayOfMonth { get; set; } = 3;

        public byte SendHour { get; set; } = 8;
    }
}
