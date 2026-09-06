namespace KpiReport.Etl.Models
{
    /// <summary>
    /// แถวดิบจากไฟล์ CSV เครื่องหยุด ก่อนแปลงชนิดข้อมูลใด ๆ
    /// ทุกฟิลด์เป็น string โดยตั้งใจ ให้ตรงกับ stg.DowntimeRaw
    /// การแปลง (parse) ทั้งหมดทำที่ core.usp_Transform_Downtime ฝั่ง SQL
    /// </summary>
    public class DowntimeRawRow
    {
        public string SourceFileName { get; set; }
        public int? SourceLineNo { get; set; }

        public string EventDate { get; set; }
        public string DepartmentText { get; set; }
        public string MachineCode { get; set; }
        public string ReasonCode { get; set; }
        public string ReasonText { get; set; }
        public string StartTimeText { get; set; }
        public string EndTimeText { get; set; }
        public string DurationMinText { get; set; }
    }
}
