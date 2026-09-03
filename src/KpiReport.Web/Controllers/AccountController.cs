using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using KpiReport.Web.Infrastructure;
using KpiReport.Web.Models;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin.Security;

namespace KpiReport.Web.Controllers
{
    /// <summary>
    /// จัดการการเข้าสู่ระบบของเว็บภายใน
    ///
    /// ระบบนี้ตั้งใจให้มีทางเข้าออกทางเดียว: Login / LogOff
    /// การสร้างและจัดการบัญชีทั้งหมดอยู่ที่ UsersController (Admin เท่านั้น)
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
