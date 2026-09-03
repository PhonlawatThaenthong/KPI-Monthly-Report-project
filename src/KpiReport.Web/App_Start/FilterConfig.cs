using System.Web.Mvc;
using KpiReport.Web.Infrastructure;

namespace KpiReport.Web
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            // แสดงหน้า Views/Shared/Error.cshtml แทน stack trace เมื่อเกิด exception
            // (ทำงานร่วมกับ <customErrors> ใน Web.config)
            filters.Add(new HandleErrorAttribute());

            // บังคับ HTTPS ทั้งเว็บ — request ที่เข้ามาทาง http จะถูก redirect ไป https
            // เปิด/ปิดด้วย Auth:RequireHttps (Release เปิดเป็นค่าตั้งต้น, Debug ปิด
            // เพื่อให้รันบน http://localhost ตอนพัฒนาได้ตามปกติ)
            if (AuthSettings.RequireHttps)
            {
                filters.Add(new RequireHttpsAttribute());
            }
        }
    }
}
