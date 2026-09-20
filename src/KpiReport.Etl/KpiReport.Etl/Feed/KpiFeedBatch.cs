using System;
using System.Collections.Generic;
using KpiReport.Etl.Models;

namespace KpiReport.Etl.Feed
{
    /// <summary>
    /// ผลการดึงข้อมูลหนึ่งชุด = ค่า KPI ของหนึ่งเดือนจากต้นทางหนึ่งครั้ง
    ///
    /// DedupeKey คือลายนิ้วมือของชุดข้อมูล (ไฟล์ mock ใช้ SHA-256 ของไฟล์)
    /// ถ้าเคยโหลดชุดที่มีลายนิ้วมือเดียวกันไปแล้วจะข้าม ไม่โหลดซ้ำ
    /// ต้นทางที่ตอบเป็น API อาจไม่มีค่านี้ -> ปล่อย null แล้วโหลดทุกครั้ง
    /// (ปลอดภัย เพราะ Transform ลบเดือนเดิมก่อนโหลดใหม่อยู่แล้ว)
    /// </summary>
    public class KpiFeedBatch
    {
        public string SourceName { get; set; }
        public string DedupeKey { get; set; }
        public long? SizeBytes { get; set; }
        public DateTime? ModifiedAtUtc { get; set; }
        public List<KpiFeedRow> Rows { get; set; }

        public KpiFeedBatch()
        {
            Rows = new List<KpiFeedRow>();
        }
    }
}
