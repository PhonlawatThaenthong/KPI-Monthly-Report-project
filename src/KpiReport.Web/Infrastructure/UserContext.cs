using System.Configuration;
using System.Data.SqlClient;
using System.Security.Principal;
using System.Web;
using Dapper;
using Microsoft.AspNet.Identity;

namespace KpiReport.Web.Infrastructure
{
    /// <summary>
    /// ตัดสินว่า user ที่ login อยู่มีสิทธิ์เห็นข้อมูลแผนกไหน
    ///
    /// *** หลักการที่ห้ามละเมิด ***
    /// ค่านี้ต้องคำนวณจากฝั่ง server โดยอ้างอิง identity ของคนที่ login เท่านั้น
    /// ห้ามรับ DepartmentId จาก query string / form / cookie แล้วเชื่อ
    /// ไม่งั้น Viewer แผนก A แค่แก้ URL เป็น ?dept=2 ก็ดูข้อมูลแผนก B ได้ทันที
    /// </summary>
    public static class UserContext
    {
        /// <summary>
        /// คืน DepartmentId ที่ user คนนี้ดูได้
        /// คืน null = ดูได้ทุกแผนก (Admin / Manager)
        /// </summary>
        public static int? GetAllowedDepartmentId(IPrincipal user)
        {
            if (user == null || !user.Identity.IsAuthenticated)
                return -999;   // ค่าที่ไม่มีอยู่จริง -> query จะไม่คืนอะไรเลย (fail closed)

            if (user.IsInRole("Admin") || user.IsInRole("Manager"))
                return null;   // ไม่กรอง

            string userId = user.Identity.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return -999;

            string connStr = ConfigurationManager.ConnectionStrings["KpiDb"].ConnectionString;
            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();
                int? deptId = conn.QueryFirstOrDefault<int?>(@"
                    SELECT TOP 1 DepartmentId
                    FROM meta.UserDepartment
                    WHERE UserId = @UserId
                    ORDER BY IsPrimary DESC, DepartmentId",
                    new { UserId = userId });

                // Viewer ที่ยังไม่ถูกผูกแผนก -> ไม่ให้เห็นอะไรเลย ปลอดภัยกว่าให้เห็นทั้งหมด
                return deptId ?? -999;
            }
        }

        public static bool CanViewAllDepartments(IPrincipal user)
        {
            return user != null
                && user.Identity.IsAuthenticated
                && (user.IsInRole("Admin") || user.IsInRole("Manager"));
        }

        /// <summary>
        /// ตรวจว่า user มีสิทธิ์ดูแผนกที่ร้องขอมาหรือไม่
        /// ใช้ตอนที่หน้าจอมีตัวเลือกให้สลับแผนก (Manager/Admin เท่านั้นที่สลับได้)
        /// </summary>
        public static bool IsDepartmentAllowed(IPrincipal user, int requestedDepartmentId)
        {
            if (CanViewAllDepartments(user))
                return true;

            int? allowed = GetAllowedDepartmentId(user);
            return allowed.HasValue && allowed.Value == requestedDepartmentId;
        }

        public static string GetClientIp()
        {
            var request = HttpContext.Current?.Request;
            if (request == null) return null;

            // ถ้ามี reverse proxy / load balancer ให้ดู X-Forwarded-For ก่อน
            string forwarded = request.Headers["X-Forwarded-For"];
            if (!string.IsNullOrEmpty(forwarded))
                return forwarded.Split(',')[0].Trim();

            return request.UserHostAddress;
        }
    }
}
