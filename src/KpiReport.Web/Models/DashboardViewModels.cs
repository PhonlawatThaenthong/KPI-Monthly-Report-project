using System.Collections.Generic;

namespace KpiReport.Web.Models
{
    /// <summary>
    /// ข้อมูล 1 การ์ด KPI พร้อมกราฟย้อนหลังของตัวเอง
    /// </summary>
    public class KpiCardViewModel
    {
        public string KpiCode { get; set; }
        public string KpiName { get; set; }
        public string KpiNameTh { get; set; }
        public string Unit { get; set; }
        public int DecimalPlaces { get; set; }
        public string Direction { get; set; }          // 'H' = ยิ่งมากยิ่งดี, 'L' = ยิ่งน้อยยิ่งดี
        public string FormulaText { get; set; }

        public decimal? ActualValue { get; set; }
        public decimal? TargetValue { get; set; }
        public decimal? AchievementPct { get; set; }
        public decimal? MoMChangePct { get; set; }
        public string StatusFlag { get; set; }          // GREEN / YELLOW / RED / null

        public List<TrendPointViewModel> Trend { get; set; } = new List<TrendPointViewModel>();
    }

    /// <summary>
    /// ข้อมูลทั้งหน้า Dashboard
    /// </summary>
    public class DashboardViewModel
    {
        public int MonthKey { get; set; }
        public string MonthLabel { get; set; }

        public int SelectedDepartmentId { get; set; }
        public bool CanSwitchDepartment { get; set; }
        public List<DepartmentOption> Departments { get; set; }

        public List<KpiCardViewModel> Kpis { get; set; } = new List<KpiCardViewModel>();
    }
}
