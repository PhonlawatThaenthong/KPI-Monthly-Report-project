using System.Collections.Generic;

namespace KpiReport.Etl.Feed
{
    /// <summary>
    /// สัญญาของ "แหล่งค่า KPI ที่คำนวณมาแล้ว"
    ///
    /// ระบบนี้ไม่คำนวณ KPI เอง หน้าที่ของชั้นนี้คือไปเอาค่ามาให้เท่านั้น
    /// ตอนนี้ยังต่อระบบจริงไม่ได้ จึงมี MockJsonKpiFeedSource อ่านไฟล์ JSON จำลอง
    /// วันที่ต่อได้จริงให้เขียน HttpKpiFeedSource ให้ครบแล้วสลับที่ App.config
    /// ส่วนที่เหลือของ pipeline (staging -> transform -> fact -> รายงาน) ไม่ต้องแก้
    /// </summary>
    public interface IKpiFeedSource
    {
        /// <summary>ข้อความสั้น ๆ บอกว่าดึงมาจากไหน ใช้เขียนลง ETL log</summary>
        string Description { get; }

        /// <summary>
        /// ดึงค่า KPI จากต้นทาง
        /// monthKey = null หมายถึงเอาทุกเดือนที่ต้นทางมี
        /// </summary>
        IList<KpiFeedBatch> Fetch(int? monthKey);
    }
}
