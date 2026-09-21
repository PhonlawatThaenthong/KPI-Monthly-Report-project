using System;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using KpiReport.Web.Models;
using KpiReport.Web.Repositories;

namespace KpiReport.Web.Controllers
{
    /// <summary>
    /// หน้าติดตามความคืบหน้าการทำ KPI รายบุคคล
    ///
    /// ตอบคำถามที่ Dashboard ตอบไม่ได้: แผนกที่ค่าเฉลี่ยผ่านเกณฑ์
    /// อาจมีคนที่ยังไม่ได้เริ่มทำเลยซ่อนอยู่ข้างใน
    ///
    /// สิทธิ์: Admin เห็นทุกแผนก / Manager เห็นเฉพาะแผนกที่ถูกผูกไว้ใน
    /// meta.UserDepartment — กรองที่ฝั่ง server ทุกครั้ง ไม่เชื่อค่าจาก URL
    /// </summary>
    [Authorize(Roles = "Admin,Manager")]
    public class MonitoringController : BaseController
    {
        private readonly MonitoringRepository _repo;
        private readonly KpiRepository _kpiRepo;

        public MonitoringController()
        {
            string connStr = ConfigurationManager.ConnectionStrings["KpiDb"].ConnectionString;
            _repo = new MonitoringRepository(connStr);
            _kpiRepo = new KpiRepository(connStr);
        }

        /// <summary>
        /// GET /Monitoring?monthKey=202608&amp;departmentIds=3&amp;departmentIds=5
        /// ภาพรวมทุกแผนกที่ผู้ใช้คนนี้ดูได้ เรียงแผนกที่ทำได้น้อยสุดขึ้นก่อน
        /// </summary>
        public ActionResult Index(int? monthKey, int[] departmentIds)
        {
            var months = _repo.GetAvailableMonths();
            if (months.Count == 0)
                return View(new MonitoringIndexViewModel { MonthKey = 0, MonthLabel = "-" });

            int resolvedMonth = monthKey.HasValue && months.Contains(monthKey.Value)
                ? monthKey.Value
                : months[0];

            string scope = ResolveDepartmentScope(departmentIds);

            var vm = new MonitoringIndexViewModel
            {
                MonthKey = resolvedMonth,
                MonthLabel = FormatMonth(resolvedMonth),
                AvailableMonths = months,
                Departments = _repo.GetDepartmentCompletion(resolvedMonth, scope),
                SelectedDepartmentIds = (departmentIds ?? new int[0]).ToList(),
                DepartmentOptions = GetSelectableDepartments()
            };

            Audit("VIEW_MONITORING", "Monitoring", resolvedMonth.ToString(),
                  detail: "Scope=" + (scope ?? "ALL"));

            return View(vm);
        }

        /// <summary>
        /// GET /Monitoring/Department/3?monthKey=202608&amp;onlyIncomplete=true
        /// ตาราง คน × KPI ของแผนกเดียว — ช่องไหนเขียวคือทำเสร็จแล้ว
        /// </summary>
        public ActionResult Department(int id, int? monthKey, bool onlyIncomplete = false)
        {
            // ตรวจสิทธิ์ก่อนแตะข้อมูล: Manager ที่ไม่ได้ดูแลแผนกนี้ต้องไม่เห็นอะไรเลย
            if (!Infrastructure.UserContext.IsDepartmentAllowed(User, id))
            {
                Audit("ACCESS_DENIED", "Department", id.ToString(),
                      detail: "พยายามเปิดหน้า Monitoring ของแผนกที่ไม่มีสิทธิ์", isSuccess: false);
                return new HttpStatusCodeResult(HttpStatusCode.Forbidden,
                    "คุณไม่มีสิทธิ์ดูข้อมูลของแผนกนี้");
            }

            var months = _repo.GetAvailableMonths();
            if (months.Count == 0)
                return RedirectToAction("Index");

            int resolvedMonth = monthKey.HasValue && months.Contains(monthKey.Value)
                ? monthKey.Value
                : months[0];

            string scope = id.ToString(CultureInfo.InvariantCulture);

            var summary = _repo.GetDepartmentCompletion(resolvedMonth, scope).FirstOrDefault();
            var employees = _repo.GetEmployeeSummary(resolvedMonth, scope);
            var kpiRows = _repo.GetEmployeeKpi(resolvedMonth, scope);

            if (onlyIncomplete)
                employees = employees.Where(e => !e.IsFullyComplete).ToList();

            var vm = new DepartmentMonitoringViewModel
            {
                MonthKey = resolvedMonth,
                MonthLabel = FormatMonth(resolvedMonth),
                DepartmentId = id,
                DepartmentName = summary != null ? summary.DepartmentName : "#" + id,
                Summary = summary,
                Employees = employees,
                OnlyIncomplete = onlyIncomplete,

                // หัวตารางเอาจาก KPI ที่พบจริงในเดือนนี้ ไม่ใช่รายการตายตัว
                // เพิ่ม KPI ตัวใหม่ที่ meta.KpiDefinition แล้วตารางขึ้นคอลัมน์ให้เอง
                KpiColumns = kpiRows
                    .GroupBy(r => r.KpiId)
                    .Select(g => g.First())
                    .OrderBy(r => r.SortOrder)
                    .ThenBy(r => r.KpiCode)
                    .ToList(),

                KpiByEmployee = kpiRows
                    .GroupBy(r => r.EmployeeId)
                    .ToDictionary(g => g.Key,
                                  g => g.OrderBy(r => r.SortOrder).ToList())
            };

            Audit("VIEW_MONITORING_DEPT", "Department", id.ToString(),
                  detail: "MonthKey=" + resolvedMonth);

            return View(vm);
        }

        /// <summary>
        /// รายชื่อแผนกสำหรับตัวกรอง — เหลือเฉพาะที่ผู้ใช้คนนี้มีสิทธิ์
        /// แถว -99 (ภาพรวมทั้งบริษัท) ไม่ใช่แผนกจริง จึงตัดออก
        /// </summary>
        private System.Collections.Generic.List<DepartmentOption> GetSelectableDepartments()
        {
            var all = _kpiRepo.GetDepartmentOptions()
                              .Where(d => d.DepartmentId > 0);

            int[] allowed = AllowedDepartmentIds;
            if (allowed != null)
                all = all.Where(d => allowed.Contains(d.DepartmentId));

            return all.OrderBy(d => d.DepartmentName).ToList();
        }

        /// <summary>202608 -> "Aug 2026" (รูปแบบเดียวกับ MonthLabel ในชั้น rpt)</summary>
        public static string FormatMonth(int monthKey)
        {
            if (monthKey < 190001) return monthKey.ToString();

            int year = monthKey / 100;
            int month = monthKey % 100;
            if (month < 1 || month > 12) return monthKey.ToString();

            return new DateTime(year, month, 1)
                .ToString("MMM yyyy", CultureInfo.GetCultureInfo("en-US"));
        }
    }
}
