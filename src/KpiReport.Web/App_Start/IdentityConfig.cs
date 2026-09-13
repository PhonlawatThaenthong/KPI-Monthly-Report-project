using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin;
using Microsoft.Owin.Security;
using KpiReport.Web.Models;

namespace KpiReport.Web
{
    /// <summary>
    /// ตัวส่งอีเมลที่ ASP.NET Identity เรียกใช้ (UserManager.SendEmailAsync)
    ///
    /// ต่อเข้ากับ SmtpMailSender ตัวเดียวกับที่งานส่งรายงานรายเดือนใช้
    /// (src/Shared/Mail/SmtpMailSender.cs ผูกเข้าโปรเจกต์ด้วย csproj Link)
    /// อ่านค่า SMTP จาก appSettings "Smtp.*" ใน Web.config (รหัสผ่านจาก
    /// environment variable KPI_SMTP_PASSWORD — ดู Shared/Mail/SmtpSettings.cs)
    ///
    /// ส่งแบบ synchronous ครอบด้วย Task.FromResult เพราะ SmtpClient
    /// รุ่นที่ใช้อยู่ไม่มี async ที่ยกเลิกได้จริง และปริมาณเมลของระบบนี้น้อยมาก
    /// การทำให้ซับซ้อนกว่านี้ไม่คุ้ม
    /// </summary>
    public class EmailService : IIdentityMessageService
    {
        public Task SendAsync(IdentityMessage message)
        {
            try
            {
                var sender = new KpiReport.Shared.Mail.SmtpMailSender();
                sender.Send(message.Destination, message.Subject, message.Body);
            }
            catch (Exception ex)
            {
                // ห้ามโยนต่อ ไม่งั้นหน้า "ลืมรหัสผ่าน" จะแสดง error
                // ซึ่งบอกคนนอกได้ว่าอีเมลที่กรอกมีอยู่จริงในระบบ
                // (ถ้าไม่มีบัญชี controller จะไม่เรียกมาถึงตรงนี้เลย)
                System.Diagnostics.Debug.WriteLine("[EmailService] ส่งเมลไม่สำเร็จ: " + ex.Message);

                Infrastructure.AuditLogger.Write("EMAIL_SEND_FAILED",
                    userName: message.Destination,
                    detail: ex.Message,
                    isSuccess: false);
            }

            return Task.FromResult(0);
        }
    }

    public class SmsService : IIdentityMessageService
    {
        public Task SendAsync(IdentityMessage message)
        {
            // Plug in your SMS service here to send a text message.
            return Task.FromResult(0);
        }
    }

    // Configure the application user manager used in this application. UserManager is defined in ASP.NET Identity and is used by the application.
    public class ApplicationUserManager : UserManager<ApplicationUser>
    {
        public ApplicationUserManager(IUserStore<ApplicationUser> store)
            : base(store)
        {
        }

        public static ApplicationUserManager Create(IdentityFactoryOptions<ApplicationUserManager> options, IOwinContext context) 
        {
            var manager = new ApplicationUserManager(new UserStore<ApplicationUser>(context.Get<ApplicationDbContext>()));
            // Configure validation logic for usernames
            manager.UserValidator = new UserValidator<ApplicationUser>(manager)
            {
                AllowOnlyAlphanumericUserNames = false,
                RequireUniqueEmail = true
            };

            // Configure validation logic for passwords
            manager.PasswordValidator = new PasswordValidator
            {
                RequiredLength = 6,
                RequireNonLetterOrDigit = true,
                RequireDigit = true,
                RequireLowercase = true,
                RequireUppercase = true,
            };

            // Configure user lockout defaults
            manager.UserLockoutEnabledByDefault = true;
            manager.DefaultAccountLockoutTimeSpan = TimeSpan.FromMinutes(5);
            manager.MaxFailedAccessAttemptsBeforeLockout = 5;

            // Register two factor authentication providers. This application uses Phone and Emails as a step of receiving a code for verifying the user
            // You can write your own provider and plug it in here.
            manager.RegisterTwoFactorProvider("Phone Code", new PhoneNumberTokenProvider<ApplicationUser>
            {
                MessageFormat = "Your security code is {0}"
            });
            manager.RegisterTwoFactorProvider("Email Code", new EmailTokenProvider<ApplicationUser>
            {
                Subject = "Security Code",
                BodyFormat = "Your security code is {0}"
            });
            manager.EmailService = new EmailService();
            manager.SmsService = new SmsService();
            var dataProtectionProvider = options.DataProtectionProvider;
            if (dataProtectionProvider != null)
            {
                manager.UserTokenProvider =
                    new DataProtectorTokenProvider<ApplicationUser>(
                        dataProtectionProvider.Create("ASP.NET Identity"))
                    {
                        // ค่า default ของ Identity คือ 1 วัน ซึ่งยาวเกินไปสำหรับลิงก์
                        // ที่วิ่งอยู่ในกล่องจดหมาย ใครเปิดกล่องเจอก็ใช้ได้
                        TokenLifespan = TimeSpan.FromHours(1)
                    };
            }
            return manager;
        }
    }

    // Configure the application sign-in manager which is used in this application.
    public class ApplicationSignInManager : SignInManager<ApplicationUser, string>
    {
        public ApplicationSignInManager(ApplicationUserManager userManager, IAuthenticationManager authenticationManager)
            : base(userManager, authenticationManager)
        {
        }

        public override Task<ClaimsIdentity> CreateUserIdentityAsync(ApplicationUser user)
        {
            return user.GenerateUserIdentityAsync((ApplicationUserManager)UserManager);
        }

        public static ApplicationSignInManager Create(IdentityFactoryOptions<ApplicationSignInManager> options, IOwinContext context)
        {
            return new ApplicationSignInManager(context.GetUserManager<ApplicationUserManager>(), context.Authentication);
        }
    }
}
