using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using KpiReport.Web.Infrastructure;
using KpiReport.Web.Models;
using KpiReport.Web.Reporting;
using KpiReport.Web.Repositories;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;

namespace KpiReport.Web.Controllers
{
    /// <summary>
    /// ตั้งค่าว่ารายงาน KPI รายเดือนจะถูกส่งไปหาใครบ้าง — Admin และ Manager
    ///
    /// ผู้รับมี 2 แบบ
    ///   1. ผูกกับบัญชีในระบบ — กรณีปกติของ HR ประจำแผนก
    ///      อีเมลยึดตามบัญชีเสมอ เปลี่ยนอีเมลในบัญชี รายงานตามไปเอง
    ///      ปิดบัญชี = หยุดส่งทันที ไม่ต้องมาปิดซ้ำที่นี่
    ///   2. อีเมลภายนอก — ผู้บริหารหรือคนนอกที่อยากได้แค่ไฟล์
    ///      ไม่ต้องสร้างบัญชีทิ้งไว้ในระบบเพียงเพื่อรับเมล
    ///
    /// ขอบเขตข้อมูล: เลือกได้หลายแผนกต่อผู้รับหนึ่งคน (เช่น แผนก 1, 3, 5)
    /// และได้อีเมลฉบับเดียวที่รวมแผนกที่เลือกไว้ ไม่เลือกเลย = ทุกแผนก
    ///
    /// Manager เลือกได้เฉพาะแผนกที่ตัวเองดูแล — กรองที่ฝั่ง server เสมอ
    /// ค่าที่ส่งมาจากฟอร์มไม่เคยถูกเชื่อตรง ๆ (ดู UserContext.FilterAllowedDepartments)
    /// </summary>
    [Authorize(Roles = "Admin,Manager")]
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

            byte day = ClampDay(model.SendDayOfMonth);
            byte hour = ClampHour(model.SendHour);

            int[] scope = AllowedScope(model.DepartmentIds);
            bool added = _subs.Add(userId, email, displayName, scope, day, hour);

            if (!added)
            {
                TempData["SubError"] = "ผู้รับรายนี้มีอยู่แล้วในขอบเขตเดียวกัน";
                return RedirectToAction("Index");
            }

            Audit("REPORT_SUB_ADDED",
                  (userId ?? email) + " · ขอบเขต=" + DescribeScope(scope)
                  + " · ส่งทุกวันที่ " + day + " เวลา " + hour.ToString("00") + ":00");

            if (TempData["SubMessage"] == null)
                TempData["SubMessage"] = "เพิ่มผู้รับรายงานเรียบร้อยแล้ว";

            return RedirectToAction("Index");
        }

        // POST: /ReportSubscriptions/Schedule
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Schedule(int id, byte sendDayOfMonth, byte sendHour)
        {
            var row = FindManageable(id);
            if (row == null) return HttpNotFound();

            byte day = ClampDay(sendDayOfMonth);
            byte hour = ClampHour(sendHour);

            _subs.SetSchedule(id, day, hour);

            Audit("REPORT_SUB_RESCHEDULED",
                  row.Email + " · " + row.ScheduleText
                  + " -> ทุกวันที่ " + day + " เวลา " + hour.ToString("00") + ":00");

            TempData["SubMessage"] = "อัปเดตเวลาส่งของ " + row.Email + " แล้ว";
            return RedirectToAction("Index");
        }

        // POST: /ReportSubscriptions/Scope
        /// <summary>
        /// เปลี่ยนว่ารายงานของผู้รับรายนี้จะรวมแผนกไหนบ้าง
        /// ไม่ติ๊กเลย = ทุกแผนก (สำหรับ Admin) หรือทุกแผนกที่ดูแล (สำหรับ Manager)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Scope(int id, int[] departmentIds)
        {
            var row = FindManageable(id);
            if (row == null) return HttpNotFound();

            int[] scope = AllowedScope(departmentIds);
            _subs.SetDepartments(id, scope);

            Audit("REPORT_SUB_SCOPE_CHANGED",
                  row.Email + " · " + row.ScopeLabel + " -> " + DescribeScope(scope));

            TempData["SubMessage"] = "อัปเดตขอบเขตแผนกของ " + row.Email + " แล้ว";
            return RedirectToAction("Index");
        }

        // POST: /ReportSubscriptions/Toggle
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Toggle(int id)
        {
            var row = FindManageable(id);
            if (row == null) return HttpNotFound();

            _subs.SetActive(id, !row.IsActive);

            Audit(row.IsActive ? "REPORT_SUB_PAUSED" : "REPORT_SUB_RESUMED",
                  row.Email + " · ขอบเขต=" + row.ScopeLabel);

            TempData["SubMessage"] = (row.IsActive ? "หยุดส่งให้ " : "กลับมาส่งให้ ") + row.Email + " แล้ว";
            return RedirectToAction("Index");
        }

        // POST: /ReportSubscriptions/SendNow
        /// <summary>
        /// ส่งรายงานเดือนล่าสุดให้ผู้รับรายนี้เดี๋ยวนี้ ไม่ต้องรอรอบตามตาราง
        ///
        /// ใช้ ReportMailer ตัวเดียวกับงานส่งอัตโนมัติ (MonthlyReportJob)
        /// ไฟล์แนบ เนื้อเมล และขอบเขตข้อมูลจึงเหมือนกันทุกประการ
        /// รวมถึงกติกาที่ว่าผู้รับที่ผูกกับแผนกเดียวไม่เห็นตัวเลขแผนกอื่น
        ///
        /// เขียน log ด้วยคีย์ KPI_Monthly_Manual: แยกจากรอบอัตโนมัติ การกดส่ง
        /// ด้วยมือจึงไม่ทำให้ผู้รับรายนี้หลุดรอบประจำเดือน และยังแยกใน log ได้
        /// ว่าฉบับไหนมาจากใครกด
        ///
        /// ผู้รับที่ถูกระงับไว้ (IsActive = 0) ยังกดส่งได้ ถือว่าผู้ดูแลตั้งใจ
        /// แต่บัญชีที่ถูกลบหรือถูกปิดใช้งานส่งไม่ได้ เพราะไม่มีอีเมลที่เชื่อถือได้
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SendNow(int id)
        {
            var row = FindManageable(id);
            if (row == null) return HttpNotFound();

            if (row.LinkedUserMissing || row.LinkedUserDisabled)
            {
                TempData["SubError"] = "ส่งไม่ได้ — " + row.SilentReason;
                return RedirectToAction("Index");
            }

            string connStr = ConfigurationManager.ConnectionStrings["KpiDb"].ConnectionString;

            int? monthKey = new ReportDeliveryRepository(connStr).GetLatestMonthKey();
            if (monthKey == null)
            {
                TempData["SubError"] = "ยังไม่มีเดือนที่มีข้อมูลรายงาน — รัน ETL ก่อน";
                return RedirectToAction("Index");
            }

            var recipient = new ReportRecipient
            {
                Email = row.Email,
                DisplayName = row.DisplayName,
                DepartmentIds = row.DepartmentIds,
                DepartmentNames = row.DepartmentNames,
                DepartmentCount = row.DepartmentCount
            };

            try
            {
                var result = new ReportMailer(connStr).Send(
                    recipient, monthKey.Value,
                    ReportMailer.ManualReportNameFor(recipient),
                    "Sent manually by " + User.Identity.Name, dryRun: false,
                    subscriptionId: row.SubscriptionId,
                    triggerType: "MANUAL",
                    triggeredBy: User.Identity.Name);

                if (!result.HasData)
                {
                    TempData["SubError"] = "ไม่มีข้อมูล KPI ของเดือน " + monthKey.Value
                                           + " ในขอบเขต " + row.ScopeLabel + " — ยังไม่ได้ส่ง";
                    return RedirectToAction("Index");
                }

                Audit("REPORT_SENT_NOW",
                      row.Email + " · ขอบเขต=" + row.ScopeLabel + " · เดือน " + monthKey.Value);

                TempData["SubMessage"] = "ส่งรายงานเดือน " + result.MonthLabel + " ให้ " + row.Email
                                         + " แล้ว — รอบอัตโนมัติเดือนนี้ยังส่งตามปกติ";
            }
            catch (Exception ex)
            {
                Audit("REPORT_SENT_NOW_FAILED", row.Email + " · " + ex.Message);
                TempData["SubError"] = "ส่งไม่สำเร็จ: " + ex.Message;
            }

            return RedirectToAction("Index");
        }

        // GET: /ReportSubscriptions/Log/5
        /// <summary>
        /// ประวัติการส่งของผู้รับ 1 ราย — ทั้งรอบอัตโนมัติและที่มีคนกดส่ง
        ///
        /// ผ่าน FindManageable เหมือน action อื่นที่รับ id จาก URL: Manager
        /// ต้องไม่เห็น log ของผู้รับที่ขอบเขตกว้างกว่าสิทธิ์ตัวเอง เพราะ log
        /// บอกชื่อแผนกที่อยู่ในรายงานแต่ละฉบับ
        /// </summary>
        public ActionResult Log(int id, int take = 200)
        {
            var row = FindManageable(id);
            if (row == null) return HttpNotFound();

            take = ClampTake(take);

            string connStr = ConfigurationManager.ConnectionStrings["KpiDb"].ConnectionString;
            var rows = new ReportDeliveryRepository(connStr)
                .GetBySubscription(id, row.Email, take);

            return View(new ReportDeliveryLogViewModel
            {
                Subscription = row,
                Rows = rows,
                Stats = DeliveryLogStats.From(rows),
                Take = take
            });
        }

        // GET: /ReportSubscriptions/Deliveries
        /// <summary>
        /// log รวมทุกการส่ง กรองตามเดือน / สถานะ / ที่มา
        ///
        /// Admin เห็นทุกแถว ส่วน Manager เห็นเฉพาะอีเมลของผู้รับที่ตัวเอง
        /// ดูแลได้ — รายชื่อนั้นคำนวณจาก CanManage ชุดเดียวกับหน้ารายชื่อ
        /// แล้วส่งให้ SQL กรอง ไม่กรองหลังดึงออกมา ไม่งั้น TOP จะนับแถวที่
        /// เขาไม่มีสิทธิ์เห็นรวมไปด้วยแล้วหน้าจอจะดูเหมือน log หาย
        /// </summary>
        public ActionResult Deliveries(int? monthKey, string status, string trigger, int take = 200)
        {
            take = ClampTake(take);

            status = Normalize(status, "SENT", "FAILED", "PENDING");
            trigger = Normalize(trigger, "SCHEDULED", "MANUAL");

            bool isAdmin = UserContext.GetAllowedDepartmentIds(User) == null;

            List<string> allowedEmails = null;
            if (!isAdmin)
            {
                allowedEmails = _subs.GetAll()
                    .Where(CanManage)
                    .Select(r => r.Email)
                    .Where(e => !string.IsNullOrEmpty(e))
                    .Distinct()
                    .ToList();
            }

            string connStr = ConfigurationManager.ConnectionStrings["KpiDb"].ConnectionString;
            var repo = new ReportDeliveryRepository(connStr);
            var rows = repo.Search(monthKey, status, trigger, allowedEmails, take);

            return View(new DeliveryLogSearchViewModel
            {
                Rows = rows,
                Stats = DeliveryLogStats.From(rows),
                AvailableMonths = repo.GetLoggedMonths(),
                MonthKey = monthKey,
                Status = status,
                TriggerType = trigger,
                Take = take,
                IsScoped = !isAdmin
            });
        }

        // POST: /ReportSubscriptions/Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            var row = FindManageable(id);
            if (row == null) return HttpNotFound();

            _subs.Delete(id);

            Audit("REPORT_SUB_REMOVED", row.Email + " · ขอบเขต=" + row.ScopeLabel);

            TempData["SubMessage"] = "ลบ " + row.Email + " ออกจากรายชื่อผู้รับแล้ว";
            return RedirectToAction("Index");
        }

        // ---------------------------------------------------------------

        private ReportSubscriptionListViewModel BuildList()
        {
            var rows = _subs.GetAll().Where(CanManage).ToList();

            var departments = _users.GetRealDepartments();
            int[] allowedDepts = UserContext.GetAllowedDepartmentIds(User);
            if (allowedDepts != null)
                departments = departments.Where(d => allowedDepts.Contains(d.DepartmentId)).ToList();
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

        /// <summary>
        /// วันที่ 1–31 เท่านั้น ค่าที่เกินจะถูกร่นลงมาเป็นวันสุดท้ายของเดือนนั้น
        /// ตอนคำนวณรอบส่ง จึงยอมให้ตั้ง 31 ได้เพื่อสื่อความหมายว่า "สิ้นเดือน"
        /// </summary>
        private static byte ClampDay(byte value)
        {
            if (value < 1) return 1;
            if (value > 31) return 31;
            return value;
        }

        private static byte ClampHour(byte value)
        {
            return value > 23 ? (byte)23 : value;
        }

        /// <summary>
        /// ผู้ใช้คนนี้ยุ่งกับผู้รับรายนี้ได้หรือไม่
        ///
        /// Admin ได้ทุกราย ส่วน Manager ได้เฉพาะรายที่ขอบเขตอยู่ในแผนกที่ตัวเองดูแล
        /// ทั้งหมด — ถ้าแตะรายที่กว้างกว่าสิทธิ์ตัวเองได้ ก็เท่ากับกดปุ่ม "ส่งเดี๋ยวนี้"
        /// แล้วเห็นตัวเลขแผนกอื่นผ่านรายงานที่ส่งออกไป
        /// </summary>
        private bool CanManage(ReportSubscriptionRow row)
        {
            int[] allowed = UserContext.GetAllowedDepartmentIds(User);
            if (allowed == null) return true;              // Admin

            if (row.DepartmentCount == 0) return false;    // ขอบเขตทั้งบริษัท

            return row.SelectedDepartmentIds.All(allowed.Contains);
        }

        /// <summary>
        /// หาแถวที่จะแก้ พร้อมตรวจสิทธิ์ — คืน null เมื่อไม่มีสิทธิ์หรือไม่พบ
        /// ทุก action ที่รับ id จากฟอร์มต้องผ่านตัวนี้ ไม่เรียก _subs.GetById ตรง ๆ
        /// </summary>
        private ReportSubscriptionRow FindManageable(int id)
        {
            var row = _subs.GetById(id);
            if (row == null || !CanManage(row)) return null;
            return row;
        }

        /// <summary>
        /// กรองแผนกที่ฟอร์มส่งมาให้เหลือเฉพาะที่ผู้ใช้คนนี้มีสิทธิ์
        ///
        /// Admin เลือกได้ทุกแผนก / Manager เลือกได้เฉพาะแผนกที่ผูกไว้
        /// ถ้า Manager ยิงฟอร์มมาพร้อม DepartmentId ที่ไม่ได้ดูแล จะถูกตัดทิ้ง
        /// เงียบ ๆ ตรงนี้ ไม่หลุดไปเป็นขอบเขตของรายงาน
        /// </summary>
        private int[] AllowedScope(int[] requested)
        {
            if (requested == null || requested.Length == 0)
            {
                // ไม่เลือกเลย = ทุกแผนก — แต่ Manager ไม่มีสิทธิ์ "ทุกแผนก"
                // จึงต้องถูกจำกัดให้เหลือเฉพาะแผนกที่ตัวเองดูแล
                int[] allowed = UserContext.GetAllowedDepartmentIds(User);
                return allowed ?? new int[0];
            }

            int[] filtered = UserContext.FilterAllowedDepartments(User, requested);
            return filtered ?? new int[0];
        }

        private string DescribeScope(int[] departmentIds)
        {
            if (departmentIds == null || departmentIds.Length == 0) return "ทุกแผนก";

            var all = _users.GetRealDepartments();
            var names = departmentIds
                .Select(id =>
                {
                    var dept = all.FirstOrDefault(d => d.DepartmentId == id);
                    return dept != null ? dept.DepartmentName : "#" + id;
                })
                .ToList();

            return string.Join(", ", names);
        }

        /// <summary>
        /// จำนวนแถว log ที่ดึงได้ต่อครั้ง — กันไม่ให้ query ทั้งตารางจาก querystring
        /// </summary>
        private static int ClampTake(int value)
        {
            if (value < 20) return 20;
            if (value > 1000) return 1000;
            return value;
        }

        /// <summary>ค่าจากตัวกรองต้องอยู่ในรายการที่อนุญาต ไม่งั้นถือว่าไม่กรอง</summary>
        private static string Normalize(string value, params string[] allowed)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            string upper = value.Trim().ToUpperInvariant();
            return allowed.Contains(upper) ? upper : null;
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
