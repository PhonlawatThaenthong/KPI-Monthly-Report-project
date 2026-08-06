using System;

namespace KpiReport.Web.Models
{
    /// <summary>
    /// ตรงกับคอลัมน์ที่ rpt.usp_GetKpiDashboard คืนมา (มาจาก rpt.vw_KpiMonthly)
    /// ชื่อ property ต้องตรงกับชื่อคอลัมน์ให้ Dapper map อัตโนมัติได้
    /// </summary>
    public class KpiDashboardRow
    {
        public int MonthKey { get; set; }
        public string MonthLabel { get; set; }
        public int KpiId { get; set; }
        public string KpiCode { get; set; }
        public string KpiName { get; set; }
        public string KpiNameTh { get; set; }
        public string CategoryName { get; set; }
        public string Unit { get; set; }
        public int DecimalPlaces { get; set; }
        public string Direction { get; set; }
        public string FormulaText { get; set; }
        public int SortOrder { get; set; }
        public int DepartmentId { get; set; }
        public string DepartmentCode { get; set; }
        public string DepartmentName { get; set; }
        public decimal? ActualValue { get; set; }
        public decimal? TargetValue { get; set; }
        public decimal? BaselineValue { get; set; }
        public decimal? PrevMonthValue { get; set; }
        public decimal? Variance { get; set; }
        public decimal? AchievementPct { get; set; }
        public decimal? MoMChangePct { get; set; }
        public string StatusFlag { get; set; }
        public DateTime CalculatedAt { get; set; }
    }

    /// <summary>
    /// ตรงกับคอลัมน์ที่ rpt.usp_GetKpiTrend คืนมา
    /// </summary>
    public class TrendPointViewModel
    {
        public int MonthKey { get; set; }
        public string MonthLabel { get; set; }
        public decimal? ActualValue { get; set; }
        public decimal? TargetValue { get; set; }
    }

    public class DepartmentOption
    {
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; }
    }
}
