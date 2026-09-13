using System;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using KpiReport.Web.Infrastructure;
using KpiReport.Web.Models;
using KpiReport.Web.Repositories;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin.Security;

namespace KpiReport.Web.Controllers
{
    /// <summary>
    /// จัดการการเข้าสู่ระบบของเว็บภายใน
    ///
    /// ระบบนี้มีทางเข้าออกทางเดียว: Login / LogOff
    /// การสร้างและจัดการบัญชีทั้งหมดอยู่ที่ UsersController (Admin เท่านั้น)
    ///
    /// มีอีกเส้นทางเดียวที่เปิดให้คนยังไม่ล็อกอินใช้ได้ คือ ลืมรหัสผ่าน
    /// ซึ่งส่ง "ลิงก์" ไปที่อีเมลของเจ้าของบัญชี ไม่ใช่รหัสผ่าน
    /// ต่างจากที่ Admin กดรีเซ็ตให้ตรงที่เส้นทางนี้ไม่มีใครนอกจากเจ้าตัวรู้รหัสใหม่
    /// จึงไม่ต้องตั้งธง MustChangePassword
    ///
    /// action ที่มากับ template ของ ASP.NET แต่ระบบนี้ไม่ได้ใช้ ถูกตัดออกทั้งหมด
    /// (ForgotPassword, ResetPassword, ConfirmEmail, SendCode, VerifyCode,
    ///  ExternalLogin ทุกตัว) เพราะยังไม่ได้ต่อ email service และไม่มี OAuth provider
    /// ปล่อยไว้ = เปิด endpoint สาธารณะที่กดแล้วพังหรือถูกใช้ยิงหา user ที่มีอยู่จริง
    ///
    /// [Authorize] ระดับ class = ทุก action ต้อง login ก่อน ยกเว้นที่ใส่ [AllowAnonymous]
    /// </summary>
    [Authorize]
    public class AccountController : Controller
    {
        private ApplicationSignInManager _signInManager;
        private ApplicationUserManager _userManager;

        public AccountController()
        {
        }

        public AccountController(ApplicationUserManager userManager, ApplicationSignInManager signInManager)
        {
            UserManager = userManager;
            SignInManager = signInManager;
        }

        public ApplicationSignInManager SignInManager
        {
            get { return _signInManager ?? HttpContext.GetOwinContext().Get<ApplicationSignInManager>(); }
            private set { _signInManager = value; }
        }

        public ApplicationUserManager UserManager
        {
            get { return _userManager ?? HttpContext.GetOwinContext().GetUserManager<ApplicationUserManager>(); }
            private set { _userManager = value; }
        }

        // ---------------------------------------------------------------
        // Login
        // ---------------------------------------------------------------

        // GET: /Account/Login
        [AllowAnonymous]
        public ActionResult Login(string returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        // POST: /Account/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Login(LoginViewModel model, string returnUrl)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // shouldLockout: true = นับความพยายามที่ล้มเหลว แล้วล็อกบัญชีชั่วคราว
            // (ตั้งไว้ที่ 5 ครั้ง / 5 นาที ใน IdentityConfig) กัน brute force
            var result = await SignInManager.PasswordSignInAsync(
                model.Email, model.Password, model.RememberMe, shouldLockout: true);

            switch (result)
            {
                case SignInStatus.Success:
                    var loggedInUser = await UserManager.FindByNameAsync(model.Email);
                    AuditLogger.Write("LOGIN",
                        userId: loggedInUser?.Id,
                        userName: model.Email,
                        isSuccess: true);

                    // รหัสที่ Admin ตั้งให้ต้องเปลี่ยนก่อนใช้งานอย่างอื่น — พาไปหน้าเปลี่ยนรหัส
                    // จากที่นี่ตรง ๆ ไม่ปล่อยให้เด้งเข้า returnUrl/Dashboard ก่อนแล้วรอ
                    // RequirePasswordChangeFilter จับ เพราะผู้ใช้จะเห็นหน้าอื่นคั่นหนึ่งจังหวะ
                    // ตัว filter ยังอยู่เป็นด่านบังคับสำหรับทางเข้าอื่น ๆ
                    if (loggedInUser != null && MustChangePassword(loggedInUser.Id))
                    {
                        TempData["ForcePasswordChange"] = RequirePasswordChangeFilter.PendingNotice;
                        return RedirectToAction("ChangePassword", "Manage");
                    }

                    return RedirectToLocal(returnUrl);

                case SignInStatus.LockedOut:
                    AuditLogger.Write("LOGIN_LOCKED",
                        userName: model.Email,
                        detail: "บัญชีถูกล็อกชั่วคราว",
                        isSuccess: false);
                    return View("Lockout");

                case SignInStatus.Failure:
                default:
                    // ข้อความเดียวกันทั้งกรณีไม่มี user และรหัสผ่านผิด
                    // ไม่งั้นคนนอกจะใช้หน้านี้ไล่เดาว่าอีเมลไหนมีอยู่จริงในระบบ
                    AuditLogger.Write("LOGIN_FAILED",
                        userName: model.Email,
                        detail: "รหัสผ่านไม่ถูกต้องหรือไม่พบบัญชี",
                        isSuccess: false);
                    ModelState.AddModelError("", "รหัสผ่านหรือชื่อผู้ใช้ไม่ถูกต้อง");
                    return View(model);
            }
        }

        // POST: /Account/LogOff
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LogOff()
        {
            AuditLogger.Write("LOGOUT",
                userId: User.Identity.GetUserId(),
                userName: User.Identity.Name);

            AuthenticationManager.SignOut(DefaultAuthenticationTypes.ApplicationCookie);
            return RedirectToAction("Login", "Account");
        }


        // ===============================================================
        // ลืมรหัสผ่าน — ส่งลิงก์ตั้งรหัสใหม่ไปที่อีเมลของเจ้าของบัญชี
        // ===============================================================

        // GET: /Account/ForgotPassword
        [AllowAnonymous]
        public ActionResult ForgotPassword()
        {
            return View();
        }

        // POST: /Account/ForgotPassword
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            string email = model.Email.Trim();
            string ip = UserContext.GetClientIp();

            // ---- ด่านที่ 1 : จำกัดจำนวนครั้ง ----
            if (PasswordResetThrottle.IsRateLimited(email, ip))
            {
                AuditLogger.Write("PASSWORD_RESET_THROTTLED",
                    userName: email,
                    detail: "เกินจำนวนครั้งที่กำหนด (" + PasswordResetThrottle.DescribeLimit() + ")",
                    isSuccess: false);

                // ตอบเหมือนกรณีสำเร็จ ไม่บอกว่าโดนจำกัด
                // ไม่งั้นคนยิงจะรู้ว่ากำลังชนเพดานและปรับจังหวะหลบได้
                return RedirectToAction("ForgotPasswordConfirmation");
            }

            // บันทึกทุกคำขอ ไม่ว่าอีเมลจะมีอยู่จริงหรือไม่
            // เพราะนี่คือตัวนับของ throttle ด้วย ถ้าบันทึกเฉพาะที่มีบัญชี
            // การไล่ยิงอีเมลมั่วจะไม่ถูกนับเลย
            AuditLogger.Write(PasswordResetThrottle.RequestedAction, userName: email);

            var user = await UserManager.FindByNameAsync(email);

            // ---- ด่านที่ 2 : ไม่บอกว่าอีเมลนี้มีบัญชีหรือไม่ ----
            //
            // ทุกเส้นทางจบที่หน้าเดียวกันเสมอ ไม่ว่าจะไม่พบบัญชี
            // หรือพบแล้วส่งเมลสำเร็จ ถ้าตอบต่างกัน หน้านี้จะกลายเป็น
            // เครื่องมือไล่ตรวจว่าอีเมลไหนเป็นพนักงานของบริษัท
            if (user != null)
            {
                string code = await UserManager.GeneratePasswordResetTokenAsync(user.Id);

                string callbackUrl = Url.Action(
                    "ResetPassword", "Account",
                    new { userId = user.Id, code = code },
                    protocol: Request.Url.Scheme);

                await UserManager.SendEmailAsync(user.Id,
                    "ตั้งรหัสผ่านใหม่ - HR KPI System",
                    BuildResetEmail(email, callbackUrl));

                AuditLogger.Write("PASSWORD_RESET_SENT",
                    userId: user.Id,
                    userName: email);
            }

            return RedirectToAction("ForgotPasswordConfirmation");
        }

        // GET: /Account/ForgotPasswordConfirmation
        [AllowAnonymous]
        public ActionResult ForgotPasswordConfirmation()
        {
            return View();
        }

        // ===============================================================
        // ตั้งรหัสผ่านใหม่จากลิงก์ในอีเมล
        // ===============================================================

        // GET: /Account/ResetPassword?userId=...&code=...
        [AllowAnonymous]
        public ActionResult ResetPassword(string userId, string code)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
            {
                return View("ResetPasswordInvalid");
            }

            return View(new ResetPasswordViewModel { Code = code, UserId = userId });
        }

        // POST: /Account/ResetPassword
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await UserManager.FindByIdAsync(model.UserId);
            if (user == null)
            {
                // ไม่พบบัญชี ตอบเหมือนกรณีลิงก์หมดอายุ ไม่ยืนยันว่ามีบัญชีนี้หรือไม่
                return View("ResetPasswordInvalid");
            }

            var result = await UserManager.ResetPasswordAsync(user.Id, model.Code, model.Password);

            if (result.Succeeded)
            {
                // ตั้งรหัสด้วยตัวเอง ไม่ใช่ Admin ตั้งให้ จึงไม่ต้องบังคับเปลี่ยนซ้ำ
                // ต่างจากเส้นทาง Users/ResetPassword ที่ต้องตั้งธง MustChangePassword
                AuditLogger.Write("PASSWORD_RESET_COMPLETED",
                    userId: user.Id,
                    userName: user.UserName);

                return RedirectToAction("ResetPasswordConfirmation");
            }

            // token ผิดหรือหมดอายุ จะได้ error ปนกับกฎรหัสผ่าน
            // แยกออกจากกันเพื่อให้ผู้ใช้รู้ว่าต้องขอลิงก์ใหม่ ไม่ใช่แค่เปลี่ยนรหัส
            if (result.Errors.Any(e => e.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                AuditLogger.Write("PASSWORD_RESET_INVALID_TOKEN",
                    userId: user.Id, userName: user.UserName, isSuccess: false);

                return View("ResetPasswordInvalid");
            }

            AddErrors(result);
            return View(model);
        }

        // GET: /Account/ResetPasswordConfirmation
        [AllowAnonymous]
        public ActionResult ResetPasswordConfirmation()
        {
            return View();
        }

        // ---------------------------------------------------------------

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_userManager != null)
                {
                    _userManager.Dispose();
                    _userManager = null;
                }

                if (_signInManager != null)
                {
                    _signInManager.Dispose();
                    _signInManager = null;
                }
            }

            base.Dispose(disposing);
        }

        #region Helpers

        private IAuthenticationManager AuthenticationManager
        {
            get { return HttpContext.GetOwinContext().Authentication; }
        }


        /// <summary>
        /// เนื้ออีเมลลิงก์ตั้งรหัสผ่านใหม่ — สั้น ตรงประเด็น และบอกอายุลิงก์
        /// บอกด้วยว่าถ้าไม่ได้เป็นคนขอให้ทำอย่างไร เพราะอีเมลแบบนี้
        /// อาจไปถึงคนที่ไม่ได้กดเอง หากมีใครกรอกอีเมลเขาในหน้าลืมรหัสผ่าน
        /// </summary>
        private static string BuildResetEmail(string email, string callbackUrl)
        {
            string safeUrl = System.Net.WebUtility.HtmlEncode(callbackUrl);

            return
                "<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1b2430;\">" +
                "<p>มีคำขอตั้งรหัสผ่านใหม่สำหรับบัญชี <strong>" +
                System.Net.WebUtility.HtmlEncode(email) + "</strong> ในระบบ HR KPI</p>" +
                "<p><a href=\"" + safeUrl + "\" " +
                "style=\"display:inline-block;background:#1b4d89;color:#ffffff;" +
                "padding:10px 20px;border-radius:6px;text-decoration:none;font-weight:600;\">" +
                "ตั้งรหัสผ่านใหม่</a></p>" +
                "<p style=\"color:#5c6b7a;font-size:12px;\">ลิงก์นี้ใช้ได้ภายใน 1 ชั่วโมง และใช้ได้เพียงครั้งเดียว</p>" +
                "<p style=\"color:#5c6b7a;font-size:12px;\">หากคุณไม่ได้เป็นคนขอ ไม่ต้องทำอะไร " +
                "รหัสผ่านเดิมยังใช้ได้ตามปกติ หากเกิดขึ้นบ่อยครั้งกรุณาแจ้งทีม HR Analytics</p>" +
                "<p style=\"color:#9aa8b6;font-size:11px;word-break:break-all;\">" +
                "หากปุ่มกดไม่ได้ ให้คัดลอกลิงก์นี้ไปวางในเบราว์เซอร์<br />" + safeUrl + "</p>" +
                "</div>";
        }

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error);
            }
        }

        /// <summary>
        /// กัน open redirect: รับเฉพาะ URL ภายในเว็บนี้เท่านั้น
        /// ถ้าไม่ตรวจ คนร้ายส่งลิงก์ /Account/Login?returnUrl=http://evil.example
        /// แล้วเหยื่อจะถูกพาไปหน้าปลอมทันทีหลัง login สำเร็จ
        /// </summary>
        private static bool MustChangePassword(string userId)
        {
            string connStr = ConfigurationManager.ConnectionStrings["KpiDb"].ConnectionString;
            return new UserAdminRepository(connStr).MustChangePassword(userId);
        }

        private ActionResult RedirectToLocal(string returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Dashboard");
        }

        #endregion
    }
}
