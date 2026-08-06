namespace KpiReport.Etl.Models
{
    /// <summary>
    /// แถวดิบจากไฟล์ CSV ลงเวลา ตรงกับ stg.AttendanceRaw
    /// </summary>
    public class AttendanceRawRow
    {
        public string SourceFileName { get; set; }
        public int? SourceLineNo { get; set; }

        public string WorkDate { get; set; }
        public string EmployeeCode { get; set; }
        public string EmployeeName { get; set; }
        public string DepartmentText { get; set; }
        public string StatusText { get; set; }
        public string WorkHoursText { get; set; }
        public string OtHoursText { get; set; }
    }
}
