using System;
using KpiReport.Web.Infrastructure;
using KpiReport.Web.Models;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin;
using Microsoft.Owin.Security.Cookies;
using Owin;

namespace KpiReport.Web
{
    public partial class Startup
    {
        public void ConfigureAuth(IAppBuilder app)
        {
            // ใช้ instance เดียวต่อ request
            app.CreatePerOwinContext(ApplicationDbContext.Create);
            app.CreatePerOwinContext<ApplicationUserManager>(ApplicationUserManager.Create);
            app.CreatePerOwinContext<ApplicationSignInManager>(ApplicationSignInManager.Create);

            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AuthenticationType = DefaultAuthenticationTypes.ApplicationCookie,
                LoginPath = new PathString("/Account/Login"),

                // ตั้งชื่อคุกกี้เอง ไม่ใช้ค่า default ที่บอกใบ้ว่าเว็บนี้เป็น ASP.NET Identity
                CookieName = "KpiReport.Auth",

                // อายุ session — sliding คือขยับออกไปเรื่อย ๆ ระหว่างที่ยังใช้งาน
                // แต่ถ้าปล่อยทิ้งไว้เกินเวลานี้ต้อง login ใหม่
                ExpireTimeSpan = TimeSpan.FromMinutes(AuthSettings.SessionMinutes),
                SlidingExpiration = true,

                // JavaScript อ่านคุกกี้นี้ไม่ได้ ลดผลกระทบถ้ามีช่องโหว่ XSS
                CookieHttpOnly = true,

                // ส่งคุกกี้เฉพาะบน HTTPS เมื่อเปิด Auth:RequireHttps
                // (Release build เปิดเป็นค่าตั้งต้น, Debug ปิดไว้เพื่อให้รันบน localhost ได้)
                CookieSecure = AuthSettings.RequireHttps
                    ? CookieSecureOption.Always
                    : CookieSecureOption.SameAsRequest,

                // กันคุกกี้ถูกแนบไปกับ request ที่มาจากเว็บอื่น (CSRF อีกชั้นหนึ่ง)
                // Lax ยังให้กดลิงก์จากอีเมล/แชทเข้ามาแล้วยัง login อยู่
                CookieSameSite = SameSiteMode.Lax,

                Provider = new CookieAuthenticationProvider
                {
                    // ตรวจ security stamp เป็นระยะ ถ้า Admin เปลี่ยนรหัสผ่านหรือถอด role
                    // ให้ใคร คนนั้นจะถูกเตะออกภายในรอบถัดไป ไม่ต้องรอ session หมดอายุ
                    OnValidateIdentity = SecurityStampValidator.OnValidateIdentity<ApplicationUserManager, ApplicationUser>(
                        validateInterval: TimeSpan.FromMinutes(AuthSettings.ValidateIdentityMinutes),
                        regenerateIdentity: (manager, user) => user.GenerateUserIdentityAsync(manager))
                }
            });

            // ระบบนี้ไม่ได้ใช้ external login (Google/Facebook/...) และไม่ได้ใช้ two-factor
            // middleware ของสองอย่างนั้นถูกตัดออก พร้อมกับ action ใน AccountController
        }
    }
}
