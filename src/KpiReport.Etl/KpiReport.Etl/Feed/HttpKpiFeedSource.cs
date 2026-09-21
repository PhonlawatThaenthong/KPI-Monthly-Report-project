using System;
using System.Collections.Generic;

namespace KpiReport.Etl.Feed
{
    /// <summary>
    /// ที่ทางสำหรับการต่อ API จริงของระบบ KPI ต้นทาง — ยังทำไม่ได้ในตอนนี้
    ///
    /// จงใจใส่ไว้เป็นโครงเปล่าแทนที่จะเดา endpoint แล้วเขียนโค้ดที่ไม่มีทางถูก
    /// สิ่งที่ต้องได้จากเจ้าของระบบก่อนเขียนต่อ
    ///   1. URL + วิธียืนยันตัวตน (API key / OAuth / Windows auth)
    ///   2. รูปแบบ response และรหัสตัวชี้วัด (จะได้ตั้ง meta.KpiDefinition.SourceMetricCode ให้ตรง)
    ///   3. ค่าที่ส่งกลับเป็นของเดือนไหน แก้ย้อนหลังได้หรือไม่ และหน่วยของแต่ละตัว
    ///
    /// เมื่อเขียนเสร็จ ให้ตั้ง App.config key 'KpiFeed:Provider' เป็น 'http'
    /// ส่วนอื่นของระบบไม่ต้องแก้แม้แต่บรรทัดเดียว เพราะคุยกันผ่าน IKpiFeedSource
    ///
    /// *** อย่าเก็บ API key ไว้ใน App.config *** ให้อ่านจาก environment variable
    /// KPI_FEED_APIKEY ตามแบบเดียวกับ KPI_SMTP_PASSWORD ที่ใช้อยู่
    /// </summary>
    public class HttpKpiFeedSource : IKpiFeedSource
    {
        private readonly string _baseUrl;

        public HttpKpiFeedSource(string baseUrl)
        {
            _baseUrl = baseUrl;
        }

        public string Description
        {
            get { return "HTTP API: " + (_baseUrl ?? "(ไม่ได้ตั้งค่า)"); }
        }

        public IList<KpiFeedBatch> Fetch(int? monthKey)
        {
            throw new NotImplementedException(
                "ยังเชื่อมต่อ API ของระบบ KPI ต้นทางไม่ได้ " +
                "ตอนนี้ให้ตั้ง App.config key 'KpiFeed:Provider' เป็น 'mock' " +
                "เพื่อใช้ข้อมูลจำลองจากโฟลเดอร์ mock-data/kpi-feed ไปก่อน");
        }
    }
}
