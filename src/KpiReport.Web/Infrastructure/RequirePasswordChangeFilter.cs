using System.Configuration;
using System.Web.Mvc;
using System.Web.Routing;
using KpiReport.Web.Repositories;
using Microsoft.AspNet.Identity;

namespace KpiReport.Web.Infrastructure
{
    /// <summary>
    /// บังคับให้ผู้ใช้ที่ติดธง MustChangePassword เปลี่ยนรหัสก่อนใช้งานอย่างอื่น
    ///
    /// ธงถูกตั้งเมื่อ Admin เป็นคนกำหนดรหัสให้ (สร้างบัญชีใหม่ หรือ reset)
    /// filter นี้คือสิ่งที่ทำให้รหัสที่ Admin รู้ "ใช้ได้ครั้งเดียว" จริง ๆ
    /// ไม่ใช่แค่คำแนะนำบนหน้าจอที่ผู้ใช้จะกดข้ามก็ได้
    ///
    /// หน้าที่ปล่อยผ่าน: หน้าเปลี่ยนรหัสเอง, login/logout, และไฟล์ static
    /// ถ้าไม่ยกเว้นหน้าเปลี่ยนรหัส จะ redirect วนไม่รู้จบ
    /// </summary>
    public class RequirePasswordChangeFilter : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var user = filterContext.HttpContext.User;
            if (user == null || !user.Identity.IsAuthenticated) return;

            // action ที่ยกเว้น ต้องเข้าถึงได้เสมอ ไม่งั้นผู้ใช้จะติดอยู่ในลูป
            var route = filterContext.RouteData.Values;
            string controller = (route["controller"] ?? "").ToString();
            string action = (route["action"] ?? "").ToString();

            if (IsAllowedWhilePending(controller, action)) return;

            // AJAX ไม่ควรถูก redirect เงียบ ๆ เพราะจะได้ HTML กลับไปแทน JSON
            if (filterContext.HttpContext.Request.IsAjaxRequest())
            {
                filterContext.Result = new HttpStatusCodeResult(403, "Password change required");
                return;
            }

            string userId = user.Identity.GetUserId();
            if (string.IsNullOrEmpty(userId)) return;

            string connStr = ConfigurationManager.ConnectionStrings["KpiDb"].ConnectionString;
            var repo = new UserAdminRepository(connStr);

            if (!repo.MustChangePassword(userId)) return;

            filterContext.Controller.TempData["ForcePasswordChange"] =
                "รหัสผ่านปัจจุบันถูกตั้งให้โดยผู้ดูแลระบบ กรุณาตั้งรหัสใหม่ของคุณเองก่อนใช้งานต่อ";

            filterContext.Result = new RedirectToRouteResult(
                new RouteValueDictionary(new { controller = "Manage", action = "ChangePassword" }));
        }

        public void OnActionExecuted(ActionExecutedContext filterContext)
        {
        }

        private static bool IsAllowedWhilePending(string controller, string action)
        {
            if (controller.Equals("Manage", System.StringComparison.OrdinalIgnoreCase)
                && action.Equals("ChangePassword", System.StringComparison.OrdinalIgnoreCase))
                return true;

            if (controller.Equals("Account", System.StringComparison.OrdinalIgnoreCase)
                && (action.Equals("Login", System.StringComparison.OrdinalIgnoreCase)
                    || action.Equals("LogOff", System.StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }
    }
}
