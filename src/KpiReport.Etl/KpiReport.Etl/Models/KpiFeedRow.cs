namespace KpiReport.Etl.Models
{
    /// <summary>
    /// KPI ของพนักงานหนึ่งคน หนึ่งตัวชี้วัด หนึ่งเดือน ที่ดึงมาจากระบบต้นทาง
    /// ตรงกับ stg.KpiEmployeeFeedRaw หนึ่งแถว
    ///
    /// หน่วยข้อมูลเป็น "รายบุคคล" ไม่ใช่รายแผนก — ค่าระดับแผนกได้จากการ
    /// rollup ในฐานข้อมูล (core.usp_Rollup_KpiEmployeeToDept) จึงไม่มีทาง
    /// ที่ตัวเลขสองระดับจะไม่ตรงกัน
    ///
    /// ทุกฟิลด์เป็น string โดยตั้งใจ — ชั้น staging รับข้อมูลดิบตามที่ต้นทางส่งมา
    /// ไม่แปลงชนิดตรงนี้ เพราะค่าที่เพี้ยนหนึ่งแถวต้องไม่ทำให้ทั้งรอบล้ม
    /// การตรวจและแปลงเป็นหน้าที่ของ core.usp_Transform_KpiEmployeeFeed
    /// </summary>
    public class KpiFeedRow
    {
        public string SourceName { get; set; }
        public int? SourceLineNo { get; set; }

        public string MonthText { get; set; }          // 202606 หรือ 2026-06
        public string EmployeeCodeText { get; set; }   // EMP-0001 (คีย์ที่ใช้จับคู่จริง)
        public string EmployeeNameText { get; set; }   // ไว้อ่านตอนดู reject log เท่านั้น
        public string DepartmentText { get; set; }     // รหัส/ชื่อแผนกตามที่ต้นทางสะกด
        public string KpiCodeText { get; set; }        // รหัสตัวชี้วัดฝั่งต้นทาง
        public string TargetValueText { get; set; }
        public string ActualValueText { get; set; }
        public string StatusText { get; set; }         // DONE / IN_PROGRESS / NOT_STARTED (ว่างได้)
        public string CompletedDateText { get; set; }
    }
}
