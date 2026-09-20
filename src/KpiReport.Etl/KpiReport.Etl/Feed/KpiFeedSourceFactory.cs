using System;
using System.Configuration;

namespace KpiReport.Etl.Feed
{
    /// <summary>
    /// เลือกแหล่งข้อมูล KPI ตาม App.config key 'KpiFeed:Provider'
    ///   mock (ค่าเริ่มต้น) = อ่านไฟล์ JSON จำลองจาก 'KpiFeed:MockFolder'
    ///   http              = เรียก API ของระบบต้นทางที่ 'KpiFeed:BaseUrl'
    ///
    /// จุดเดียวในระบบที่รู้ว่าข้อมูลมาจากไหน — ที่เหลือเห็นแค่ IKpiFeedSource
    /// </summary>
    public static class KpiFeedSourceFactory
    {
        public static IKpiFeedSource Create()
        {
            string provider = (ConfigurationManager.AppSettings["KpiFeed:Provider"] ?? "mock")
                .Trim().ToLowerInvariant();

            switch (provider)
            {
                case "mock":
                    return new MockJsonKpiFeedSource(
                        ConfigurationManager.AppSettings["KpiFeed:MockFolder"]);

                case "http":
                    return new HttpKpiFeedSource(
                        ConfigurationManager.AppSettings["KpiFeed:BaseUrl"]);

                default:
                    throw new ConfigurationErrorsException(
                        "App.config key 'KpiFeed:Provider' = '" + provider +
                        "' ไม่รู้จัก ใช้ได้เฉพาะ 'mock' หรือ 'http'");
            }
        }
    }
}
