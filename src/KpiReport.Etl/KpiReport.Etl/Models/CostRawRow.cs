namespace KpiReport.Etl.Models
{
    /// <summary>
    /// แถวดิบจาก Excel ต้นทุน ตรงกับ stg.CostRaw
    /// </summary>
    public class CostRawRow
    {
        public string SourceFileName { get; set; }
        public string SourceSheetName { get; set; }
        public int? SourceRowNo { get; set; }

        public string PeriodText { get; set; }
        public string DepartmentText { get; set; }
        public string CostTypeText { get; set; }
        public string AmountText { get; set; }
        public string CurrencyText { get; set; }
        public string Remark { get; set; }
    }
}
