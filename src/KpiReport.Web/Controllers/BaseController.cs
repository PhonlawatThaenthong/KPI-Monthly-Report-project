using System.Web.Mvc;
using KpiReport.Web.Infrastructure;
using Microsoft.AspNet.Identity;

namespace KpiReport.Web.Controllers
{
    /// <summary>
    /// Controller แม่ที่ทุกหน้าในระบบสืบทอด
    /// รวมสิ่งที่ทุกหน้าต้องใช้ไว้ที่เดียว จะได้ไม่เขียนซ้ำ
    ///
    /// [Authorize] ระดับ class = ทุก action ต้อง login ก่อน
    /// ถ้าหน้าไหนต้องเปิดสาธารณะ ให้ใส่ [AllowAnonymous] เฉพาะ action นั้น
    /// การตั้งค่าแบบนี้ปลอดภัยกว่าการไล่ใส่ [Authorize] ทีละหน้า
    /// เพราะถ้าลืมใส่ หน้านั้นจะกลายเป็นสาธารณะโดยไม่ตั้งใจ
    /// </summary>
    [Authorize]
    public abstract class BaseController : Controller
    {
        /// <summary>
        /// null = ดูได้ทุกแผนก | มีค่า = ดูได้เฉพาะแผนกนั้น
        /// </summary>
        protected int? AllowedDepartmentId
        {
            get { return UserContext.GetAllowedDepartmentId(User); }
        }

        protected bool CanViewAllDepartments
        {
            get { return UserContext.CanViewAllDepartments(User); }
        }

        protected string CurrentUserId
        {
            get { return User?.Identity?.GetUserId(); }
        }

        protected string CurrentUserName
        {
            get { return User?.Identity?.Name; }
        }

        /// <summary>
        /// ตัดสินว่าจะใช้ DepartmentId ไหนในการ query
        /// รับค่าที่ผู้ใช้เลือกมาได้ แต่ต้องผ่านการตรวจสิทธิ์ก่อนเสมอ
        /// </summary>
        protected int? ResolveDepartmentFilter(int? requestedDepartmentId)
        {
            if (!requestedDepartmentId.HasValue)
                return AllowedDepartmentId;

            if (UserContext.IsDepartmentAllowed(User, requestedDepartmentId.Value))
                return requestedDepartmentId;

            // ขอดูแผนกที่ไม่มีสิทธิ์ -> บันทึกไว้แล้วบังคับกลับไปใช้ของตัวเอง
            AuditLogger.Write(
                actionType: "ACCESS_DENIED",
                userId: CurrentUserId,
                userName: CurrentUserName,
                entityName: "Department",
                entityKey: requestedDepartmentId.Value.ToString(),
                detail: "พยายามเข้าถึงแผนกที่ไม่มีสิทธิ์",
                isSuccess: false);

            return AllowedDepartmentId;
        }

        protected void Audit(string actionType, string entityName = null,
                              string entityKey = null, string detail = null,
                              bool isSuccess = true)
        {
            AuditLogger.Write(actionType, CurrentUserId, CurrentUserName,
                              entityName, entityKey, detail, isSuccess);
        }
    }
}
