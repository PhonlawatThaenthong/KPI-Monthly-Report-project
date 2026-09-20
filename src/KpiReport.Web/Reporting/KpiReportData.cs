using System;
using System.Collections.Generic;
using System.Globalization;
using KpiReport.Web.Models;

namespace KpiReport.Web.Reporting
{
    /// <summary>
    /// ข้อมูลชุดเดียวที่ทั้ง Excel และ PDF ใช้ร่วมกัน
    /// controller เตรียมให้ครั้งเดียว แล้วส่งต่อให้ builder ตัวไหนก็ได้
    /// ทำแบบนี้เพื่อกันไม่ให้ Excel กับ PDF ดึงข้อมูลคนละชุดจนตัวเลขไม่ตรงกัน
    /// </summary>
    public class KpiReportData
    {
        public int MonthKey { get; set; }
        public string MonthLabel { get; set; }

        /// <summary>ชื่อขอบเขตข้อมูลที่ export เช่น "All Departments" หรือชื่อแผนก</summary>
        public string ScopeLabel { get; set; }

        public string GeneratedBy { get; set; }
        public DateTime GeneratedAt { get; set; }

        /// <summary>KPI ตามขอบเขตที่ผู้ใช้เห็น (ภาพรวม หรือแผนกเดียว)</summary>
        public List<KpiDashboardRow> Rows { get; set; } = new List<KpiDashboardRow>();

        /// <summary>
        /// KPI แยกรายแผนก — มีเฉพาะแผนกที่ผู้ใช้/ผู้รับรายนั้นมีสิทธิ์เห็นเท่านั้น
        /// Manager ที่ดูแลแผนกเดียวจะเป็น null เสมอ เพื่อไม่ให้ข้อมูลแผนกอื่นหลุดออกไป
        /// </summary>
        public List<KpiDashboardRow> DepartmentRows { get; set; }

        /// <summary>
        /// true = Rows เป็นข้อมูลแยกรายแผนกอยู่แล้ว (ผู้รับเลือกไว้หลายแผนก)
        ///
        /// กรณีนี้ตาราง "KPI Detail" แบบแบนจะซ้ำกับตารางแยกรายแผนก และอ่านแล้ว
        /// สับสนเพราะ KPI ชื่อเดียวกันโผล่หลายครั้งโดยไม่บอกว่าของแผนกไหน
        /// จึงให้ builder ข้ามตารางแบนไป เหลือเฉพาะตารางแยกรายแผนก
        /// ส่วนตัวเลขสรุปด้านบนยังนับจาก Rows ตามปกติ (นับทุกแผนกที่เลือก)
        /// </summary>
        public bool RowsAreDepartmentBreakdown { get; set; }

        public bool HasDepartmentBreakdown
        {
            get { return DepartmentRows != null && DepartmentRows.Count > 0; }
        }

        public int CountGreen { get { return Count("GREEN"); } }
        public int CountYellow { get { return Count("YELLOW"); } }
        public int CountRed { get { return Count("RED"); } }

        private int Count(string flag)
        {
            int n = 0;
            foreach (var r in Rows)
            {
                if (string.Equals(r.StatusFlag, flag, StringComparison.OrdinalIgnoreCase)) n++;
            }
            return n;
        }

        /// <summary>ชื่อไฟล์มาตรฐาน เช่น KPI_Report_202606.xlsx</summary>
        public string FileName(string extension)
        {
            return "KPI_Report_" + MonthKey + "." + extension;
        }

        /// <summary>
        /// รายงานเป็นภาษาอังกฤษล้วน จึงต้อง format ด้วย InvariantCulture เสมอ
        ///
        /// ถ้าปล่อยให้ใช้ culture ของเครื่อง (เช่น th-TH) จะได้ชื่อเดือนภาษาไทย
        /// และปีพุทธศักราช ซึ่งฟอนต์ Arial ใน PDF วาดไม่ได้ กลายเป็นสี่เหลี่ยม
        /// </summary>
        public static readonly CultureInfo ReportCulture = CultureInfo.InvariantCulture;

        /// <summary>วันที่/เวลาที่พิมพ์บนหัวรายงาน เช่น "03 Sep 2026 13:12"</summary>
        public string GeneratedAtText
        {
            get { return GeneratedAt.ToString("dd MMM yyyy HH:mm", ReportCulture); }
        }

        /// <summary>จัดรูปแบบตัวเลขตาม DecimalPlaces ของ KPI แต่ละตัว (ค่าว่างเป็น "-")</summary>
        public static string Format(decimal? value, int decimals)
        {
            return value.HasValue ? value.Value.ToString("N" + decimals, ReportCulture) : "-";
        }

        public static string FormatPct(decimal? value)
        {
            return value.HasValue ? value.Value.ToString("N1", ReportCulture) + "%" : "-";
        }
    }
}
