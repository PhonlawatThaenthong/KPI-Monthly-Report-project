using System.Configuration;
using System.Linq;
using System.Web.Mvc;
using KpiReport.Web.Models;
using KpiReport.Web.Repositories;

namespace KpiReport.Web.Controllers
{
    public class DashboardController : BaseController
    {
        private readonly KpiRepository _repo;

        public DashboardController()
        {
            string connStr = ConfigurationManager.ConnectionStrings["KpiDb"].ConnectionString;
            _repo = new KpiRepository(connStr);
        }

        /// <summary>
        /// GET /Dashboard?monthKey=202606&amp;departmentId=2
        ///
        /// monthKey ไม่ระบุ    -> ใช้เดือนล่าสุดที่มีข้อมูล
        /// departmentId ไม่ระบุ -> Viewer เห็นแผนกตัวเอง / Admin,Manager เห็นภาพรวมทั้งบริษัท
        /// departmentId ที่ Viewer ไม่มีสิทธิ์ -> ถูกบังคับกลับไปที่แผนกตัวเอง (ดู BaseController)
        /// </summary>
        public ActionResult Index(int? monthKey, int? departmentId)
        {
            int? latestMonth = _repo.GetLatestMonthKey();

            if (latestMonth == null)
            {
                // ยังไม่มีข้อมูล KPI เลยในระบบ (เช่น ETL ยังไม่เคยรัน)
                return View(new DashboardViewModel
                {
                    MonthKey = 0,
                    MonthLabel = "-",
                    Kpis = new System.Collections.Generic.List<KpiCardViewModel>()
                });
            }

            int resolvedMonthKey = monthKey ?? latestMonth.Value;

            // ค่าที่คืนจาก ResolveDepartmentFilter:
            //   null  = ไม่ถูกจำกัด (Admin/Manager ที่ยังไม่ได้เลือกแผนก) -> แสดงภาพรวม (-99)
            //   ตัวเลข = ต้องใช้ค่านี้เท่านั้น (ของ Viewer หรือค่าที่ Admin/Manager เลือกเอง)
            int? deptFilter = ResolveDepartmentFilter(departmentId);
            int effectiveDepartmentId = deptFilter ?? -99;

            var rows = _repo.GetDashboard(resolvedMonthKey, effectiveDepartmentId);

            var vm = new DashboardViewModel
            {
                MonthKey = resolvedMonthKey,
                MonthLabel = rows.Any() ? rows.First().MonthLabel : resolvedMonthKey.ToString(),
                SelectedDepartmentId = effectiveDepartmentId,
                CanSwitchDepartment = CanViewAllDepartments,
                Departments = CanViewAllDepartments ? _repo.GetDepartmentOptions() : null
            };

            foreach (var row in rows.OrderBy(r => r.SortOrder))
            {
                var trend = _repo.GetTrend(row.KpiCode, effectiveDepartmentId, 12);

                vm.Kpis.Add(new KpiCardViewModel
                {
                    KpiCode = row.KpiCode,
                    KpiName = row.KpiName,
                    KpiNameTh = row.KpiNameTh,
                    Unit = row.Unit,
                    DecimalPlaces = row.DecimalPlaces,
                    Direction = row.Direction,
                    FormulaText = row.FormulaText,
                    ActualValue = row.ActualValue,
                    TargetValue = row.TargetValue,
                    AchievementPct = row.AchievementPct,
                    MoMChangePct = row.MoMChangePct,
                    StatusFlag = row.StatusFlag,
                    Trend = trend
                });
            }

            Audit("VIEW_DASHBOARD", "Dashboard",
                  resolvedMonthKey.ToString(),
                  detail: $"DepartmentId={effectiveDepartmentId}");

            return View(vm);
        }
    }
}
