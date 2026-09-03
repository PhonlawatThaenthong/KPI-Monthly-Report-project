using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using KpiReport.Web.Infrastructure;
using KpiReport.Web.Models;
using KpiReport.Web.Repositories;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;

namespace KpiReport.Web.Controllers
{
    /// <summary>
    /// ตั้งค่าว่ารายงาน KPI รายเดือนจะถูกส่งไปหาใครบ้าง — Admin เท่านั้น
    ///
    /// ผู้รับมี 2 แบบ
    ///   1. ผูกกับบัญชีในระบบ — กรณีปกติของ HR ประจำแผนก
    ///      อีเมลยึดตามบัญชีเสมอ เปลี่ยนอีเมลในบัญชี รายงานตามไปเอง
    ///      ปิดบัญชี = หยุดส่งทันที ไม่ต้องมาปิดซ้ำที่นี่
    ///   2. อีเมลภายนอก — ผู้บริหารหรือคนนอกที่อยากได้แค่ไฟล์
    ///      ไม่ต้องสร้างบัญชีทิ้งไว้ในระบบเพียงเพื่อรับเมล
    ///
    /// ขอบเขตข้อมูลใช้กติกาเดียวกับสิทธิ์ในเว็บ: ผูกกับแผนกไหน
    /// ได้เฉพาะแผนกนั้น ไม่ผูกแผนก = ได้ภาพรวม + แยกรายแผนก
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class ReportSubscriptionsController : Controller
    {
        private readonly ReportSubscriptionRepository _subs;
        private readonly UserAdminRepository _users;
        private ApplicationUserManager _userManager;

        public ReportSubscriptionsController()
        {
            string connStr = ConfigurationManager.ConnectionStrings["KpiDb"].ConnectionString;
            _subs = new ReportSubscriptionRepository(connStr);
            _users = new UserAdminRepository(connStr);
        }

        public ApplicationUserManager UserManager
        {
            get { return _userManager ?? HttpContext.GetOwinContext().GetUserManager<ApplicationUserManager>(); }
            private set { _userManager = value; }
        }

        // GET: /ReportSubscriptions
        public ActionResult Index()
        {
            return View(BuildList());
        }

        // POST: /ReportSubscriptions/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Add(ReportSubscriptionCreateViewModel model)
        {
            string userId = null;
            string email = null;
            string displayName = string.IsNullOrWhiteSpace(model.DisplayName)
                ? null
                : model.DisplayName.Trim();

            if (model.SourceType == "External")
            {
                if (string.IsNullOrWhiteSpace(model.Email))
                {
                    TempData["SubError"] = "กรุณากรอกอีเมล";
                    return RedirectToAction("Index");
                }

                email = model.Email.Trim();

                // ถ้าอีเมลนั้นมีบัญชีอยู่แล้ว ผูกเป็นบัญชีให้เลยดีกว่า
                // ไม่งั้นจะได้ผู้รับที่ไม่หยุดส่งเองตอนบัญชีถูกปิด
                var existing = UserManager.FindByName(email);
                if (existing != null)
                {
                    userId = existing.Id;
                    email = null;
                    TempData["SubMessage"] = "อีเมลนี้มีบัญชีในระบบอยู่แล้ว จึงผูกเป็นบัญชีให้แทน";
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(model.UserId))
                {
                    TempData["SubError"] = "กรุณาเลือกบัญชีผู้ใช้";
                    return RedirectToAction("Index");
                }

                var user = UserManager.FindById(model.UserId);
                if (user == null)
                {
                    TempData["SubError"] = "ไม่พบบัญชีที่เลือก";
                    return RedirectToAction("Index");
                }

                userId = user.Id;
            }

            bool added = _subs.Add(userId, email, displayName, model.DepartmentId);

            if (!added)
            {
                TempData["SubError"] = "ผู้รับรายนี้มีอยู่แล้วในขอบเขตเดียวกัน";
                return RedirectToAction("Index");
            }

            Audit("REPORT_SUB_ADDED",
                  (userId ?? email) + " · ขอบเขต=" + DescribeScope(model.DepartmentId));

            if (TempData["SubMessage"] == null)
                TempData["SubMessage"] = "เพิ่มผู้รับรายงานเรียบร้อยแล้ว";

            return RedirectToAction("Index");
        }

        // POST: /ReportSubscriptions/Toggle
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Toggle(int id)
        {
            var row = _subs.GetById(id);
            if (row == null) return HttpNotFound();

            _subs.SetActive(id, !row.IsActive);

            Audit(row.IsActive ? "REPORT_SUB_PAUSED" : "REPORT_SUB_RESUMED",
                  row.Email + " · ขอบเขต=" + row.ScopeLabel);

            TempData["SubMessage"] = (row.IsActive ? "หยุดส่งให้ " : "กลับมาส่งให้ ") + row.Email + " แล้ว";
            return RedirectToAction("Index");
        }

        // POST: /ReportSubscriptions/Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            var row = _subs.GetById(id);
            if (row == null) return HttpNotFound();

            _subs.Delete(id);

            Audit("REPORT_SUB_REMOVED", row.Email + " · ขอบเขต=" + row.ScopeLabel);

            TempData["SubMessage"] = "ลบ " + row.Email + " ออกจากรายชื่อผู้รับแล้ว";
            return RedirectToAction("Index");
        }

        // ---------------------------------------------------------------

        private ReportSubscriptionListViewModel BuildList()
        {
            var rows = _subs.GetAll();
            var departments = _users.GetRealDepartments();
            var alreadySubscribed = _subs.GetSubscribedUserIds();
            var deptByUser = _users.GetDepartmentByUser();

            var options = new List<UserOption>();

            foreach (var user in UserManager.Users.OrderBy(u => u.UserName).ToList())
            {
                // ผู้ใช้ที่เป็นผู้รับอยู่แล้วไม่ต้องโผล่ใน dropdown ซ้ำ
                if (alreadySubscribed.Contains(user.Id)) continue;

                DepartmentOption dept;
                deptByUser.TryGetValue(user.Id, out dept);

                options.Add(new UserOption
                {
                    UserId = user.Id,
                    Email = user.UserName,
                    DepartmentId = dept != null ? dept.DepartmentId : (int?)null,
                    Label = dept != null
                        ? user.UserName + " — " + dept.DepartmentName
                        : user.UserName + " — ทุกแผนก"
                });
            }

            return new ReportSubscriptionListViewModel
            {
                Rows = rows,
                Departments = departments,
                AvailableUsers = options,
                CountReceiving = rows.Count(r => r.WillReceive),
                CountSilent = rows.Count(r => !r.WillReceive)
            };
        }

        private string DescribeScope(int? departmentId)
        {
            if (!departmentId.HasValue) return "ทุกแผนก";

            var dept = _users.GetRealDepartments()
                             .FirstOrDefault(d => d.DepartmentId == departmentId.Value);

            return dept != null ? dept.DepartmentName : "#" + departmentId.Value;
        }

        private void Audit(string actionType, string detail)
        {
            AuditLogger.Write(actionType,
                userId: User.Identity.GetUserId(),
                userName: User.Identity.Name,
                entityName: "ReportSubscription",
                detail: detail);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _userManager != null)
            {
                _userManager.Dispose();
                _userManager = null;
            }

            base.Dispose(disposing);
        }
    }
}
