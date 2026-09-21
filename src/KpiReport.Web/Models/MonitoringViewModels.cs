using System.Collections.Generic;
using System.Linq;

namespace KpiReport.Web.Models
{
    /// <summary>
    /// 1 แถวจาก rpt.vw_DepartmentKpiCompletion — ภาพรวมของแผนกหนึ่งในเดือนหนึ่ง
    /// </summary>
    public class DepartmentCompletionRow
    {
        public int MonthKey { get; set; }
        public int DepartmentId { get; set; }
        public string DepartmentCode { get; set; }
        public string DepartmentName { get; set; }

        public int EmployeeCount { get; set; }
        public int EmployeeCompleteCount { get; set; }
        public int EmployeeIncompleteCount { get; set; }

        public int TotalKpi { get; set; }
        public int DoneKpi { get; set; }
        public int InProgressKpi { get; set; }
        public int NotStartedKpi { get; set; }

        public decimal? KpiCompletionPct { get; set; }
        public decimal? EmployeeCompletionPct { get; set; }

        /// <summary>
        /// สีของแถบความคืบหน้า — ใช้เกณฑ์เดียวกับ StatusFlag ของ KPI ระดับแผนก
        /// เพื่อไม่ให้หน้าจอสองหน้าบอกสถานะขัดกันเอง
        /// </summary>
        public string StatusFlag
        {
            get
            {
                decimal pct = KpiCompletionPct ?? 0m;
                if (pct >= 100m) return "GREEN";
                if (pct >= 80m) return "YELLOW";
                return "RED";
            }
        }
    }

    /// <summary>1 แถวจาก rpt.vw_EmployeeKpiSummary — พนักงานหนึ่งคนในเดือนหนึ่ง</summary>
    public class EmployeeCompletionRow
    {
        public int MonthKey { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; }
        public string EmployeeName { get; set; }
        public string Position { get; set; }
        public int DepartmentId { get; set; }
        public string DepartmentCode { get; set; }
        public string DepartmentName { get; set; }

        public int TotalKpi { get; set; }
        public int DoneCount { get; set; }
        public int InProgressCount { get; set; }
        public int NotStartedCount { get; set; }
        public decimal? CompletionPct { get; set; }
        public bool IsFullyComplete { get; set; }
    }

    /// <summary>1 แถวจาก rpt.vw_EmployeeKpiStatus — KPI หนึ่งตัวของพนักงานหนึ่งคน</summary>
    public class EmployeeKpiRow
    {
        public int MonthKey { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; }
        public string EmployeeName { get; set; }
        public string Position { get; set; }
        public int DepartmentId { get; set; }
        public string DepartmentCode { get; set; }
        public string DepartmentName { get; set; }

        public int KpiId { get; set; }
        public string KpiCode { get; set; }
        public string KpiName { get; set; }
        public string KpiNameTh { get; set; }
        public string Unit { get; set; }
        public byte DecimalPlaces { get; set; }
        public string Direction { get; set; }
        public bool IsChecklist { get; set; }
        public int SortOrder { get; set; }

        public decimal? TargetValue { get; set; }
        public decimal? ActualValue { get; set; }
        public decimal? AchievementPct { get; set; }
        public string CompletionStatus { get; set; }
        public bool IsComplete { get; set; }
        public System.DateTime? CompletedDate { get; set; }

        public string DisplayName
        {
            get { return string.IsNullOrEmpty(KpiNameTh) ? KpiName : KpiNameTh; }
        }

        /// <summary>ค่าที่แสดงในตาราง — KPI แบบเช็กไม่มีตัวเลขให้แสดง</summary>
        public string ValueText
        {
            get
            {
                if (IsChecklist)
                    return CompletionStatus == "DONE" ? "✓" : "—";

                if (!ActualValue.HasValue) return "—";

                return ActualValue.Value.ToString("N" + DecimalPlaces)
                       + (string.IsNullOrEmpty(Unit) ? "" : " " + Unit);
            }
        }

        public string StatusCssClass
        {
            get
            {
                switch (CompletionStatus)
                {
                    case "DONE": return "kpi-cell-done";
                    case "IN_PROGRESS": return "kpi-cell-progress";
                    default: return "kpi-cell-none";
                }
            }
        }

        public string StatusTextTh
        {
            get
            {
                switch (CompletionStatus)
                {
                    case "DONE": return "เสร็จแล้ว";
                    case "IN_PROGRESS": return "กำลังทำ";
                    default: return "ยังไม่เริ่ม";
                }
            }
        }
    }

    /// <summary>หน้า Monitoring — ภาพรวมทุกแผนกที่ผู้ใช้คนนี้ดูได้</summary>
    public class MonitoringIndexViewModel
    {
        public int MonthKey { get; set; }
        public string MonthLabel { get; set; }
        public List<int> AvailableMonths { get; set; }
        public List<DepartmentCompletionRow> Departments { get; set; }

        /// <summary>แผนกที่ผู้ใช้เลือกกรองไว้ (ว่าง = ทุกแผนกที่มีสิทธิ์)</summary>
        public List<int> SelectedDepartmentIds { get; set; }
        public List<DepartmentOption> DepartmentOptions { get; set; }

        public MonitoringIndexViewModel()
        {
            AvailableMonths = new List<int>();
            Departments = new List<DepartmentCompletionRow>();
            SelectedDepartmentIds = new List<int>();
            DepartmentOptions = new List<DepartmentOption>();
        }

        public int TotalEmployees { get { return Departments.Sum(d => d.EmployeeCount); } }
        public int TotalComplete { get { return Departments.Sum(d => d.EmployeeCompleteCount); } }
        public int TotalIncomplete { get { return Departments.Sum(d => d.EmployeeIncompleteCount); } }

        public decimal OverallCompletionPct
        {
            get
            {
                int total = Departments.Sum(d => d.TotalKpi);
                if (total == 0) return 0m;
                return decimal.Round(100m * Departments.Sum(d => d.DoneKpi) / total, 1);
            }
        }
    }

    /// <summary>หน้า Monitoring ของแผนกเดียว — ตาราง คน × KPI</summary>
    public class DepartmentMonitoringViewModel
    {
        public int MonthKey { get; set; }
        public string MonthLabel { get; set; }
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; }

        public DepartmentCompletionRow Summary { get; set; }
        public List<EmployeeCompletionRow> Employees { get; set; }

        /// <summary>คีย์ = EmployeeId, ค่า = KPI ของคนนั้นเรียงตาม SortOrder</summary>
        public Dictionary<int, List<EmployeeKpiRow>> KpiByEmployee { get; set; }

        /// <summary>หัวตาราง — KPI ทุกตัวที่ใช้ในเดือนนี้ เรียงตาม SortOrder</summary>
        public List<EmployeeKpiRow> KpiColumns { get; set; }

        /// <summary>true = แสดงเฉพาะคนที่ยังทำไม่ครบ</summary>
        public bool OnlyIncomplete { get; set; }

        public DepartmentMonitoringViewModel()
        {
            Employees = new List<EmployeeCompletionRow>();
            KpiByEmployee = new Dictionary<int, List<EmployeeKpiRow>>();
            KpiColumns = new List<EmployeeKpiRow>();
        }
    }
}
