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

        /// <summary>null = ได้รายงานภาพรวมทั้งบริษัท + แยกรายแผนก</summary>
        public int? DepartmentId { get; set; }
        public string DepartmentName { get; set; }

        public bool IsActive { get; set; }

        /// <summary>true = ผูกกับบัญชีในระบบ (อีเมลตามบัญชีเสมอ)</summary>
        public bool IsLinkedToUser { get; set; }

        /// <summary>true = เคยผูกกับบัญชี แต่บัญชีนั้นถูกลบไปแล้ว</summary>
        public bool LinkedUserMissing { get; set; }

        /// <summary>true = บัญชีที่ผูกไว้ถูกปิดใช้งาน จึงหยุดส่งอัตโนมัติ</summary>
        public bool LinkedUserDisabled { get; set; }

        public string ScopeLabel
        {
            get { return DepartmentId.HasValue ? (DepartmentName ?? "#" + DepartmentId) : "ทุกแผนก"; }
        }

        /// <summary>สรุปว่ารอบเดือนหน้าจะได้รับจริงหรือไม่ — ต้องผ่านทุกเงื่อนไข</summary>
        public bool WillReceive
        {
            get { return IsActive && !LinkedUserMissing && !LinkedUserDisabled; }
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

        /// <summary>ว่าง = ทุกแผนก</summary>
        public int? DepartmentId { get; set; }
    }
}
