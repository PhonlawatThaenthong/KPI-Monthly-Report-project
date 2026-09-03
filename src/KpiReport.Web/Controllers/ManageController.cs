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
    /// หน้าจัดการบัญชีของตัวเอง
    ///
    /// ระบบนี้มีอย่างเดียวที่ผู้ใช้ทำเองได้: เปลี่ยนรหัสผ่าน
    ///
    /// ของเดิมมี two-factor, เบอร์โทร (ต้องมี SMS service) และ external login
    /// (ต้องมี OAuth provider) ซึ่งไม่ได้ต่ออะไรไว้เลย กดแล้วไม่เกิดอะไรหรือ error
    /// จึงตัดออกทั้งหมดพร้อมกับ action ฝั่ง AccountController
    ///
    /// อย่างอื่น — สร้างบัญชี, ให้ role, ผูกแผนก — เป็นงานของ Admin
    /// </summary>
    [Authorize]
    public class ManageController : Controller
    {
        private ApplicationSignInManager _signInManager;
        private ApplicationUserManager _userManager;

        public ManageController()
        {
        }

        public ManageController(ApplicationUserManager userManager, ApplicationSignInManager signInManager)
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

        // GET: /Manage/Index
        public ActionResult Index(ManageMessageId? message)
        {
            ViewBag.StatusMessage =
                message == ManageMessageId.ChangePasswordSuccess ? "เปลี่ยนรหัสผ่านเรียบร้อยแล้ว"
                : message == ManageMessageId.Error ? "เกิดข้อผิดพลาด กรุณาลองใหม่"
                : "";

            return View(new IndexViewModel { HasPassword = HasPassword() });
        }

        // GET: /Manage/ChangePassword
        public ActionResult ChangePassword()
        {
            return View();
        }

        // POST: /Manage/ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            string userId = User.Identity.GetUserId();
            var result = await UserManager.ChangePasswordAsync(userId, model.OldPassword, model.NewPassword);

            if (result.Succeeded)
            {
                // การเปลี่ยนรหัสผ่านทำให้ security stamp เปลี่ยน คุกกี้เดิมของทุกเครื่อง
                // จะใช้ไม่ได้ในรอบตรวจถัดไป จึงต้อง sign in ใหม่ให้เครื่องนี้
                // ไม่งั้นคนที่เพิ่งเปลี่ยนรหัสจะถูกเตะออกเองภายในไม่กี่นาที
                var user = await UserManager.FindByIdAsync(userId);
                if (user != null)
                {
                    await SignInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
                }

                AuditLogger.Write("PASSWORD_CHANGED",
                    userId: userId,
                    userName: User.Identity.Name);

                return RedirectToAction("Index", new { Message = ManageMessageId.ChangePasswordSuccess });
            }

            AuditLogger.Write("PASSWORD_CHANGE_FAILED",
                userId: userId,
                userName: User.Identity.Name,
                detail: "รหัสผ่านเดิมไม่ถูกต้องหรือรหัสใหม่ไม่ผ่านเกณฑ์",
                isSuccess: false);

            AddErrors(result);
            return View(model);
        }

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

        private bool HasPassword()
        {
            var user = UserManager.FindById(User.Identity.GetUserId());
            return user != null && user.PasswordHash != null;
        }

        public enum ManageMessageId
        {
            ChangePasswordSuccess,
            Error
        }

        #endregion
    }
}
