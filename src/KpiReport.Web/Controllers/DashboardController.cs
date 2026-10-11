using System;
using System.Configuration;
using System.Linq;
using System.Collections.Generic;
using System.Web.Mvc;
using KpiReport.Web.Infrastructure;
using KpiReport.Web.Models;
using KpiReport.Web.Reporting;
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
        /// departmentId ไม่ระบุ -> Admin เห็นแผนกแรกในรายการ / Manager เห็นแผนกหลักของตัวเอง
        /// departmentId ที่ Manager ไม่มีสิทธิ์ -> ถูกบังคับกลับไปที่แผนกตัวเอง (ดู BaseController)
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

            // ไม่มีตัวเลือก "ทุกแผนก" (-99) แล้ว — ทุกคนต้องดูทีละแผนกเสมอ
            // Admin ที่ยังไม่ได้เลือกแผนก จะได้แผนกแรกในรายการเป็นค่าเริ่มต้น
            var selectable = SelectableDepartments();
            int effectiveDepartmentId = ResolveDashboardDepartment(departmentId, selectable);

            var rows = _repo.GetDashboard(resolvedMonthKey, effectiveDepartmentId);

            var vm = new DashboardViewModel
            {
                MonthKey = resolvedMonthKey,
                MonthLabel = rows.Any() ? rows.First().MonthLabel : resolvedMonthKey.ToString(),
                SelectedDepartmentId = effectiveDepartmentId,

                // Manager ที่ดูแลหลายแผนกต้องสลับดูได้เหมือนกัน แค่จำกัดไว้
                // เฉพาะแผนกของตัวเอง — รายการนี้กรองด้วยสิทธิ์จริงจากฝั่ง server
                Departments = selectable,
                CanSwitchDepartment = selectable.Count > 1
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

        // ===============================================================
        // Export
        // ===============================================================

        /// <summary>
        /// GET /Dashboard/ExportExcel?monthKey=202606&amp;departmentId=2
        /// ไฟล์ .xlsx : sheet "Summary" + sheet "By Department" (เฉพาะคนที่เห็นได้ทุกแผนก)
        /// </summary>
        [HttpGet]
        public ActionResult ExportExcel(int? monthKey, int? departmentId)
        {
            KpiReportData data = BuildReportData(monthKey, departmentId);
            if (data == null)
                return RedirectToIndexWithNoData(monthKey, departmentId);

            byte[] bytes = ExcelReportBuilder.Build(data);

            Audit("EXPORT_EXCEL", "Dashboard", data.MonthKey.ToString(),
                  detail: "Scope=" + data.ScopeLabel + "; Rows=" + data.Rows.Count);

            return File(bytes,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        data.FileName("xlsx"));
        }

        /// <summary>
        /// GET /Dashboard/ExportPdf?monthKey=202606&amp;departmentId=2
        /// ไฟล์ .pdf : สรุปสถานะ + ตาราง KPI + แยกรายแผนก (เฉพาะคนที่เห็นได้ทุกแผนก)
        /// </summary>
        [HttpGet]
        public ActionResult ExportPdf(int? monthKey, int? departmentId)
        {
            KpiReportData data = BuildReportData(monthKey, departmentId);
            if (data == null)
                return RedirectToIndexWithNoData(monthKey, departmentId);

            byte[] bytes = PdfReportBuilder.Build(data);

            Audit("EXPORT_PDF", "Dashboard", data.MonthKey.ToString(),
                  detail: "Scope=" + data.ScopeLabel + "; Rows=" + data.Rows.Count);

            return File(bytes, "application/pdf", data.FileName("pdf"));
        }

        // ---------------------------------------------------------------

        /// <summary>
        /// เตรียมข้อมูลชุดเดียวให้ทั้ง Excel และ PDF ใช้ร่วมกัน
        ///
        /// สำคัญ: ใช้ ResolveDepartmentFilter ตัวเดียวกับหน้า Dashboard
        /// ดังนั้น Manager ของแผนก A จะ export ได้เฉพาะข้อมูลแผนก A
        /// และจะไม่ได้ส่วน "By Department" เพราะ CanViewAllDepartments เป็น false
        ///
        /// คืน null เมื่อยังไม่มีข้อมูล KPI ในระบบ
        /// </summary>
        private KpiReportData BuildReportData(int? monthKey, int? departmentId)
        {
            int? latestMonth = _repo.GetLatestMonthKey();
            if (latestMonth == null)
                return null;

            int resolvedMonthKey = monthKey ?? latestMonth.Value;

            int effectiveDepartmentId =
                ResolveDashboardDepartment(departmentId, SelectableDepartments());

            var rows = _repo.GetDashboard(resolvedMonthKey, effectiveDepartmentId)
                            .OrderBy(r => r.SortOrder)
                            .ToList();

            var data = new KpiReportData
            {
                MonthKey = resolvedMonthKey,
                MonthLabel = rows.Any() ? rows.First().MonthLabel : resolvedMonthKey.ToString(),
                ScopeLabel = ResolveScopeLabel(effectiveDepartmentId, rows),
                GeneratedBy = CurrentUserName,
                GeneratedAt = DateTime.Now,
                Rows = rows
            };

            // ส่วนแยกรายแผนกให้เฉพาะคนที่มีสิทธิ์เห็นทุกแผนกเท่านั้น
            if (CanViewAllDepartments)
            {
                var deptRows = _repo.GetByDepartment(resolvedMonthKey)
                                    .OrderBy(r => r.DepartmentName)
                                    .ThenBy(r => r.SortOrder)
                                    .ToList();

                if (deptRows.Count > 0)
                    data.DepartmentRows = deptRows;
            }

            return data;
        }

        /// <summary>
        /// แผนกที่ผู้ใช้คนนี้สลับดูได้
        /// Admin ได้ทุกแผนกจริง / Manager ได้เฉพาะแผนกที่ผูกไว้
        /// แถว -99 (ภาพรวมทั้งบริษัท) ตัดออกเสมอ — หลักเดียวกับหน้า Monitoring
        /// </summary>
        private List<DepartmentOption> SelectableDepartments()
        {
            var all = _repo.GetDepartmentOptions()
                           .Where(d => d.DepartmentId > 0);

            int[] allowed = AllowedDepartmentIds;
            if (allowed != null)
                all = all.Where(d => allowed.Contains(d.DepartmentId));

            return all.OrderBy(d => d.DepartmentName).ToList();
        }

        /// <summary>
        /// แผนกที่จะใช้ query จริง
        ///   1) ผ่าน ResolveDepartmentFilter ก่อนเสมอ (ตรวจสิทธิ์ + audit ACCESS_DENIED)
        ///   2) ค่าที่ได้ต้องอยู่ในรายการที่เลือกได้ ไม่งั้นใช้แผนกแรกในรายการ
        ///      — ครอบกรณี Admin ไม่ได้เลือก (null) และกรณีส่ง -99 มาจาก URL/bookmark เก่า
        ///   3) ไม่มีแผนกให้เลือกเลย -> NoDepartment (fail closed, ไม่เห็นข้อมูล)
        /// </summary>
        private int ResolveDashboardDepartment(int? requestedDepartmentId, List<DepartmentOption> selectable)
        {
            int? deptFilter = ResolveDepartmentFilter(requestedDepartmentId);

            if (deptFilter.HasValue && selectable.Any(d => d.DepartmentId == deptFilter.Value))
                return deptFilter.Value;

            return selectable.Count > 0
                ? selectable[0].DepartmentId
                : UserContext.NoDepartment;
        }

        /// <summary>ชื่อขอบเขตที่จะพิมพ์บนหัวรายงาน</summary>
        private string ResolveScopeLabel(int effectiveDepartmentId, List<KpiDashboardRow> rows)
        {
            if (effectiveDepartmentId == -99)
                return "All Departments";

            var named = rows.FirstOrDefault(r => !string.IsNullOrEmpty(r.DepartmentName));
            if (named != null)
                return named.DepartmentName;

            if (_repo != null)
            {
                var option = SelectableDepartments()
                                  .FirstOrDefault(d => d.DepartmentId == effectiveDepartmentId);
                if (option != null)
                    return option.DepartmentName;
            }

            return "Department #" + effectiveDepartmentId;
        }

        private ActionResult RedirectToIndexWithNoData(int? monthKey, int? departmentId)
        {
            TempData["ExportMessage"] = "No KPI data available to export.";
            return RedirectToAction("Index", new { monthKey = monthKey, departmentId = departmentId });
        }
    }
}
