namespace KpiReport.Etl.Reports
{
    /// <summary>1 แถวจาก meta.vw_ActiveReportSubscription</summary>
    public class ReportSubscription
    {
        public int SubscriptionId { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }

        /// <summary>null = ได้รายงานภาพรวมทั้งบริษัท + แยกรายแผนก</summary>
        public int? DepartmentId { get; set; }

        public string DepartmentName { get; set; }

        public bool IsCompanyWide
        {
            get { return !DepartmentId.HasValue; }
        }

        /// <summary>ชื่อขอบเขตที่จะพิมพ์บนหัวรายงานและใช้เป็นคีย์กันส่งซ้ำ</summary>
        public string ScopeLabel
        {
            get { return IsCompanyWide ? "All Departments" : (DepartmentName ?? "#" + DepartmentId); }
        }
    }
}
