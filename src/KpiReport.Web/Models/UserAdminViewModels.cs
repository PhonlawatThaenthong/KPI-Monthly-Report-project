using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace KpiReport.Web.Models
{
    /// <summary>1 แถวในตารางรายชื่อผู้ใช้</summary>
    public class UserRowViewModel
    {
        public string UserId { get; set; }
        public string Email { get; set; }

        /// <summary>Admin / Manager / Viewer หรือว่างถ้ายังไม่ได้กำหนด</summary>
        public string Role { get; set; }

        public int? DepartmentId { get; set; }
        public string DepartmentName { get; set; }

        /// <summary>true = ถูกปิดใช้งาน (login ไม่ได้)</summary>
        public bool IsDisabled { get; set; }

        /// <summary>true = แถวนี้คือคนที่กำลังใช้งานอยู่ ใช้กันไม่ให้แก้ตัวเอง</summary>
        public bool IsCurrentUser { get; set; }

        /// <summary>
        /// Viewer ที่ยังไม่ผูกแผนก จะ login ได้แต่ไม่เห็นข้อมูลอะไรเลย
        /// (UserContext คืน -999 เมื่อหาแผนกไม่เจอ) ต้องเตือนให้เห็นในตาราง
        /// </summary>
        public bool NeedsAttention
        {
            get
            {
                if (IsDisabled) return false;
                if (string.IsNullOrEmpty(Role)) return true;
                return Role == "Viewer" && !DepartmentId.HasValue;
            }
        }
    }

    public class UserListViewModel
    {
        /// <summary>เฉพาะแถวของหน้าปัจจุบัน</summary>
        public List<UserRowViewModel> Users { get; set; } = new List<UserRowViewModel>();

        /// <summary>คำค้นที่ผู้ใช้พิมพ์ (ค้นจากอีเมล)</summary>
        public string Query { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        /// <summary>จำนวนแถวหลังกรองด้วยคำค้นแล้ว</summary>
        public int TotalCount { get; set; }

        /// <summary>จำนวนบัญชีทั้งหมดในระบบ ไม่สนใจคำค้น</summary>
        public int TotalUsers { get; set; }

        // สถิติ 3 ตัวนี้นับจากบัญชี "ทั้งหมด" ไม่ใช่เฉพาะหน้าที่เปิดอยู่
        // ไม่งั้นตัวเลข Needs setup จะเปลี่ยนไปมาตามหน้าที่เลื่อนดู ซึ่งไร้ประโยชน์
        public int CountActive { get; set; }
        public int CountDisabled { get; set; }
        public int CountNeedsAttention { get; set; }

        public bool IsFiltered
        {
            get { return !string.IsNullOrWhiteSpace(Query); }
        }

        public int TotalPages
        {
            get
            {
                if (PageSize <= 0 || TotalCount == 0) return 1;
                return (TotalCount + PageSize - 1) / PageSize;
            }
        }

        public bool HasPrevious { get { return Page > 1; } }
        public bool HasNext { get { return Page < TotalPages; } }

        /// <summary>ลำดับแถวแรกของหน้านี้ (นับจาก 1) ใช้แสดงข้อความ "1–20 จาก 57"</summary>
        public int FirstRowNumber
        {
            get { return TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1; }
        }

        public int LastRowNumber
        {
            get { return Math.Min(Page * PageSize, TotalCount); }
        }
    }

    public class UserCreateViewModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; }

        [Required]
        [StringLength(100, MinimumLength = 6,
            ErrorMessage = "รหัสผ่านต้องยาวอย่างน้อย {2} ตัวอักษร")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "รหัสผ่านทั้งสองช่องไม่ตรงกัน")]
        public string ConfirmPassword { get; set; }

        [Required(ErrorMessage = "ต้องเลือกสิทธิ์")]
        [Display(Name = "Role")]
        public string Role { get; set; }

        /// <summary>จำเป็นเฉพาะเมื่อ Role = Viewer</summary>
        [Display(Name = "Department")]
        public int? DepartmentId { get; set; }

        public List<DepartmentOption> Departments { get; set; } = new List<DepartmentOption>();
    }

    public class UserEditViewModel
    {
        public string UserId { get; set; }
        public string Email { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsCurrentUser { get; set; }

        [Required(ErrorMessage = "ต้องเลือกสิทธิ์")]
        [Display(Name = "Role")]
        public string Role { get; set; }

        [Display(Name = "Department")]
        public int? DepartmentId { get; set; }

        public List<DepartmentOption> Departments { get; set; } = new List<DepartmentOption>();
    }

    public class ResetPasswordAdminViewModel
    {
        public string UserId { get; set; }
        public string Email { get; set; }

        [Required]
        [StringLength(100, MinimumLength = 6,
            ErrorMessage = "รหัสผ่านต้องยาวอย่างน้อย {2} ตัวอักษร")]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare("NewPassword", ErrorMessage = "รหัสผ่านทั้งสองช่องไม่ตรงกัน")]
        public string ConfirmPassword { get; set; }
    }
}
