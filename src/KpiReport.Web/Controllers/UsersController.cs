using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using KpiReport.Web.Infrastructure;
using KpiReport.Web.Models;
using KpiReport.Web.Repositories;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using Microsoft.AspNet.Identity.Owin;

namespace KpiReport.Web.Controllers
{
    /// <summary>
    /// หน้าจัดการผู้ใช้ — Admin เท่านั้น
    ///
    /// รวมงานที่เดิมต้องเปิด SSMS ทำเองไว้ในหน้าเว็บ:
    /// สร้างบัญชี · เปลี่ยนสิทธิ์ · ย้ายแผนก · รีเซ็ตรหัสผ่าน · ปิด/เปิดใช้งาน
    ///
    /// *** สิ่งที่ตั้งใจไม่ทำ ***
    /// ไม่มีปุ่มลบบัญชีถาวร เพราะ audit log อ้าง UserId ไว้
    /// ลบทิ้งแล้วประวัติจะชี้ไปยัง user ที่ไม่มีตัวตน — ใช้ "ปิดใช้งาน" แทน
    ///
    /// *** กันยิงเท้าตัวเอง ***
    /// Admin แก้ role ตัวเอง ปิดบัญชีตัวเอง หรือถอดสิทธิ์ Admin คนสุดท้ายไม่ได้
    /// ไม่งั้นจะไม่เหลือใครเข้าหน้านี้ได้อีกเลย ต้องไปแก้ที่ฐานข้อมูลอย่างเดียว
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class UsersController : Controller
    {
        private static readonly string[] AllRoles = { "Admin", "Manager", "Viewer" };

        /// <summary>ค่าที่ใส่ใน LockoutEndDateUtc เพื่อ "ปิดใช้งานถาวร"</summary>
        private static readonly DateTimeOffset DisabledUntil =
            new DateTimeOffset(new DateTime(9999, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        private const int PageSize = 20;

        private readonly UserAdminRepository _repo;
        private ApplicationUserManager _userManager;
        private ApplicationDbContext _db;

        /// <summary>
        /// context ของ Identity ใช้สำหรับ "อ่าน" รายชื่อผู้ใช้เป็นชุดเดียว
        ///
        /// การเขียน (สร้างบัญชี เปลี่ยน role รหัสผ่าน) ยังผ่าน UserManager เหมือนเดิม
        /// เพราะมันดูแล hash และ security stamp ให้ — ที่นี่ใช้แค่ตอน query เท่านั้น
        /// </summary>
        private ApplicationDbContext Db
        {
            get { return _db ?? (_db = new ApplicationDbContext()); }
        }

        public UsersController()
        {
            string connStr = ConfigurationManager.ConnectionStrings["KpiDb"].ConnectionString;
            _repo = new UserAdminRepository(connStr);
        }

        public ApplicationUserManager UserManager
        {
            get { return _userManager ?? HttpContext.GetOwinContext().GetUserManager<ApplicationUserManager>(); }
            private set { _userManager = value; }
        }

        // ===============================================================
        // รายชื่อผู้ใช้
        // ===============================================================

        // GET: /Users?q=somchai&page=2
        public ActionResult Index(string q, int page = 1)
        {
            // ---- อ่านผู้ใช้ทั้งหมดใน query เดียว ----
            //
            // เดิมโค้ดวน UserManager.GetRoles() ทีละคน = 1 + N รอบไป-กลับฐานข้อมูล
            // IdentityUser มี navigation Roles อยู่แล้ว ดึงมาพร้อมกันทีเดียวได้
            // ตอนนี้เหลือ 2 query คงที่ ไม่ว่าจะมีผู้ใช้กี่คน
            //
            // ดึงเฉพาะ 3 คอลัมน์ที่ใช้จริง ไม่ลากทั้ง entity มา
            var roleNameById = Db.Roles.ToDictionary(r => r.Id, r => r.Name);

            var rawUsers = Db.Users
                .Select(u => new
                {
                    u.Id,
                    u.UserName,
                    u.LockoutEndDateUtc,
                    RoleIds = u.Roles.Select(r => r.RoleId)
                })
                .OrderBy(u => u.UserName)
                .ToList();

            var deptByUser = _repo.GetDepartmentByUser();
            string currentUserId = User.Identity.GetUserId();

            var allRows = new List<UserRowViewModel>();

            foreach (var u in rawUsers)
            {
                string roleId = u.RoleIds.FirstOrDefault();
                string roleName = null;
                if (roleId != null) roleNameById.TryGetValue(roleId, out roleName);

                DepartmentOption dept;
                deptByUser.TryGetValue(u.Id, out dept);

                allRows.Add(new UserRowViewModel
                {
                    UserId = u.Id,
                    Email = u.UserName,
                    Role = roleName,
                    DepartmentId = dept != null ? dept.DepartmentId : (int?)null,
                    DepartmentName = dept != null ? dept.DepartmentName : null,
                    IsDisabled = IsDisabled(u.LockoutEndDateUtc),
                    IsCurrentUser = u.Id == currentUserId
                });
            }

            // ---- กรอง / แบ่งหน้า ----
            //
            // ทำในหน่วยความจำ ไม่ใช่ใน SQL โดยตั้งใจ:
            // ข้อมูลทั้งชุดมาแล้วใน query เดียวและมีแค่ 3 คอลัมน์เล็ก ๆ
            // ส่วนสถิติด้านบน (Needs setup ฯลฯ) ต้องนับจากทุกแถวอยู่ดี
            // ถ้าวันหนึ่งผู้ใช้แตะหลักหมื่น ค่อยย้าย where/skip/take ลงไปที่ฐานข้อมูล
            var visible = allRows;
            if (!string.IsNullOrWhiteSpace(q))
            {
                string keyword = q.Trim();
                visible = allRows
                    .Where(r => r.Email != null &&
                                r.Email.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            var vm = new UserListViewModel
            {
                Query = q,
                PageSize = PageSize,
                TotalCount = visible.Count,
                TotalUsers = allRows.Count,

                // สถิตินับจากทุกบัญชี ไม่ผูกกับคำค้นหรือหน้าที่เปิดอยู่
                CountActive = allRows.Count(r => !r.IsDisabled),
                CountDisabled = allRows.Count(r => r.IsDisabled),
                CountNeedsAttention = allRows.Count(r => r.NeedsAttention)
            };

            // กันหน้าเกินขอบ เช่นค้างอยู่หน้า 5 แล้วพิมพ์คำค้นจนเหลือหน้าเดียว
            if (page < 1) page = 1;
            if (page > vm.TotalPages) page = vm.TotalPages;
            vm.Page = page;

            vm.Users = visible.Skip((page - 1) * PageSize).Take(PageSize).ToList();

            return View(vm);
        }

        // ===============================================================
        // สร้างบัญชีใหม่
        // ===============================================================

        // GET: /Users/Create
        public ActionResult Create()
        {
            return View(new UserCreateViewModel
            {
                Role = "Viewer",
                Departments = _repo.GetRealDepartments()
            });
        }

        // POST: /Users/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(UserCreateViewModel model)
        {
            ValidateRoleAndDepartment(model.Role, model.DepartmentId);

            if (!ModelState.IsValid)
            {
                model.Departments = _repo.GetRealDepartments();
                return View(model);
            }

            var user = new ApplicationUser { UserName = model.Email, Email = model.Email };
            var result = await UserManager.CreateAsync(user, model.Password);

            if (!result.Succeeded)
            {
                AddErrors(result);
                model.Departments = _repo.GetRealDepartments();
                return View(model);
            }

            UserManager.AddToRole(user.Id, model.Role);
            _repo.SetDepartment(user.Id, model.Role == "Viewer" ? model.DepartmentId : null);

            Audit("USER_CREATED", user.Id,
                  model.Email + " · role=" + model.Role + " · dept=" + DescribeDept(model.DepartmentId));

            TempData["UserMessage"] = "สร้างบัญชี " + model.Email + " เรียบร้อยแล้ว";
            return RedirectToAction("Index");
        }

        // ===============================================================
        // แก้สิทธิ์ / แผนก
        // ===============================================================

        // GET: /Users/Edit/{id}
        public ActionResult Edit(string id)
        {
            var user = FindUser(id);
            if (user == null) return HttpNotFound();

            return View(new UserEditViewModel
            {
                UserId = user.Id,
                Email = user.UserName,
                Role = UserManager.GetRoles(user.Id).FirstOrDefault(),
                DepartmentId = _repo.GetDepartmentId(user.Id),
                IsDisabled = IsDisabled(user.LockoutEndDateUtc),
                IsCurrentUser = user.Id == User.Identity.GetUserId(),
                Departments = _repo.GetRealDepartments()
            });
        }

        // POST: /Users/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(UserEditViewModel model)
        {
            var user = FindUser(model.UserId);
            if (user == null) return HttpNotFound();

            string currentRole = UserManager.GetRoles(user.Id).FirstOrDefault();
            bool isSelf = user.Id == User.Identity.GetUserId();

            if (isSelf && model.Role != currentRole)
            {
                ModelState.AddModelError("", "เปลี่ยนสิทธิ์ของตัวเองไม่ได้ ให้ Admin คนอื่นเป็นคนเปลี่ยนให้");
            }

            if (currentRole == "Admin" && model.Role != "Admin" && CountAdmins() <= 1)
            {
                ModelState.AddModelError("", "ถอดสิทธิ์ Admin คนสุดท้ายไม่ได้ ต้องมี Admin อย่างน้อย 1 คนเสมอ");
            }

            ValidateRoleAndDepartment(model.Role, model.DepartmentId);

            if (!ModelState.IsValid)
            {
                model.Email = user.UserName;
                model.IsDisabled = IsDisabled(user.LockoutEndDateUtc);
                model.IsCurrentUser = isSelf;
                model.Departments = _repo.GetRealDepartments();
                return View(model);
            }

            if (model.Role != currentRole)
            {
                foreach (string role in UserManager.GetRoles(user.Id))
                    UserManager.RemoveFromRole(user.Id, role);

                UserManager.AddToRole(user.Id, model.Role);

                Audit("USER_ROLE_CHANGED", user.Id,
                      user.UserName + " · " + (currentRole ?? "(ไม่มี)") + " -> " + model.Role);
            }

            int? newDept = model.Role == "Viewer" ? model.DepartmentId : null;
            int? oldDept = _repo.GetDepartmentId(user.Id);

            if (newDept != oldDept)
            {
                _repo.SetDepartment(user.Id, newDept);
                Audit("USER_DEPT_CHANGED", user.Id,
                      user.UserName + " · " + DescribeDept(oldDept) + " -> " + DescribeDept(newDept));
            }

            // สิทธิ์เปลี่ยนแล้วต้องบังคับให้ cookie เดิมใช้ไม่ได้
            // ไม่งั้นคนที่ถูกลดสิทธิ์จะยังใช้สิทธิ์เดิมได้จนกว่าจะถึงรอบตรวจถัดไป
            UserManager.UpdateSecurityStamp(user.Id);

            TempData["UserMessage"] = "อัปเดต " + user.UserName + " เรียบร้อยแล้ว";
            return RedirectToAction("Index");
        }

        // ===============================================================
        // รีเซ็ตรหัสผ่าน
        // ===============================================================

        // GET: /Users/ResetPassword/{id}
        public ActionResult ResetPassword(string id)
        {
            var user = FindUser(id);
            if (user == null) return HttpNotFound();

            return View(new ResetPasswordAdminViewModel { UserId = user.Id, Email = user.UserName });
        }

        // POST: /Users/ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ResetPassword(ResetPasswordAdminViewModel model)
        {
            var user = FindUser(model.UserId);
            if (user == null) return HttpNotFound();

            if (!ModelState.IsValid)
            {
                model.Email = user.UserName;
                return View(model);
            }

            // ถอดรหัสเดิมออกแล้วใส่ใหม่ ตรงไปตรงมากว่าการออก reset token
            // ซึ่งต้องตั้ง UserTokenProvider เพิ่มและมีไว้สำหรับส่งลิงก์ทางอีเมล
            var removed = await UserManager.RemovePasswordAsync(user.Id);
            if (!removed.Succeeded)
            {
                AddErrors(removed);
                model.Email = user.UserName;
                return View(model);
            }

            var added = await UserManager.AddPasswordAsync(user.Id, model.NewPassword);
            if (!added.Succeeded)
            {
                AddErrors(added);
                model.Email = user.UserName;
                return View(model);
            }

            // เตะทุก session เดิมของบัญชีนี้ออก
            UserManager.UpdateSecurityStamp(user.Id);

            Audit("USER_PASSWORD_RESET", user.Id, user.UserName);

            TempData["UserMessage"] =
                "รีเซ็ตรหัสผ่านของ " + user.UserName + " แล้ว — แจ้งรหัสใหม่ให้เจ้าตัวผ่านช่องทางที่ปลอดภัย และให้เปลี่ยนเองทันทีที่เข้าระบบได้";
            return RedirectToAction("Index");
        }

        // ===============================================================
        // ปิด / เปิดใช้งานบัญชี
        // ===============================================================

        // POST: /Users/ToggleActive
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ToggleActive(string id)
        {
            var user = FindUser(id);
            if (user == null) return HttpNotFound();

            if (user.Id == User.Identity.GetUserId())
            {
                TempData["UserError"] = "ปิดใช้งานบัญชีของตัวเองไม่ได้";
                return RedirectToAction("Index");
            }

            bool disabledNow = IsDisabled(user.LockoutEndDateUtc);

            if (!disabledNow
                && UserManager.IsInRole(user.Id, "Admin")
                && CountAdmins() <= 1)
            {
                TempData["UserError"] = "ปิดใช้งาน Admin คนสุดท้ายไม่ได้";
                return RedirectToAction("Index");
            }

            UserManager.SetLockoutEnabled(user.Id, true);

            if (disabledNow)
            {
                // ปลดล็อก: ตั้งเวลาสิ้นสุดเป็นอดีต
                UserManager.SetLockoutEndDate(user.Id, DateTimeOffset.UtcNow.AddMinutes(-1));
                UserManager.ResetAccessFailedCount(user.Id);
                Audit("USER_ENABLED", user.Id, user.UserName);
                TempData["UserMessage"] = "เปิดใช้งาน " + user.UserName + " แล้ว";
            }
            else
            {
                UserManager.SetLockoutEndDate(user.Id, DisabledUntil);
                UserManager.UpdateSecurityStamp(user.Id);   // เตะ session ที่ยังเปิดค้างออกด้วย
                Audit("USER_DISABLED", user.Id, user.UserName);
                TempData["UserMessage"] = "ปิดใช้งาน " + user.UserName + " แล้ว";
            }

            return RedirectToAction("Index");
        }

        // ===============================================================
        // helper
        // ===============================================================

        private ApplicationUser FindUser(string userId)
        {
            return string.IsNullOrEmpty(userId) ? null : UserManager.FindById(userId);
        }

        private static bool IsDisabled(DateTime? lockoutEndUtc)
        {
            return lockoutEndUtc.HasValue && lockoutEndUtc.Value > DateTime.UtcNow;
        }

        /// <summary>
        /// จำนวน Admin ที่ยังใช้งานได้ — ใช้กันไม่ให้ถอด/ปิด Admin คนสุดท้าย
        ///
        /// เดิมโหลด user ทั้งหมดแล้วถาม IsInRole ทีละคน (1 + N query)
        /// ตอนนี้นับจากฝั่ง role ตรง ๆ เหลือ query เดียว
        /// </summary>
        private int CountAdmins()
        {
            var adminRole = Db.Roles.FirstOrDefault(r => r.Name == "Admin");
            return adminRole == null ? 0 : adminRole.Users.Count;
        }

        private void ValidateRoleAndDepartment(string role, int? departmentId)
        {
            if (!AllRoles.Contains(role))
            {
                ModelState.AddModelError("Role", "สิทธิ์ไม่ถูกต้อง");
                return;
            }

            // Viewer ที่ไม่ผูกแผนกจะ login ได้แต่เห็นข้อมูลเป็นศูนย์
            // บังคับตั้งแต่ตอนกรอกฟอร์ม ดีกว่าปล่อยให้ไปงงทีหลังว่าทำไม dashboard ว่าง
            if (role == "Viewer" && !departmentId.HasValue)
            {
                ModelState.AddModelError("DepartmentId", "Viewer ต้องผูกกับแผนก");
            }
        }

        private string DescribeDept(int? departmentId)
        {
            if (!departmentId.HasValue) return "(ทุกแผนก / ไม่ผูก)";

            var dept = _repo.GetRealDepartments()
                            .FirstOrDefault(d => d.DepartmentId == departmentId.Value);

            return dept != null ? dept.DepartmentName : "#" + departmentId.Value;
        }

        private void Audit(string actionType, string targetUserId, string detail)
        {
            AuditLogger.Write(actionType,
                userId: User.Identity.GetUserId(),
                userName: User.Identity.Name,
                entityName: "User",
                entityKey: targetUserId,
                detail: detail);
        }

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError("", error);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_userManager != null)
                {
                    _userManager.Dispose();
                    _userManager = null;
                }

                if (_db != null)
                {
                    _db.Dispose();
                    _db = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}
