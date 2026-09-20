using System;
using System.Configuration;
using System.Linq;
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
    /// ไม่งั้น Manager ของแผนก A แค่แก้ URL เป็น ?dept=2 ก็ดูข้อมูลแผนก B ได้ทันที
    /// </summary>
    public static class UserContext
    {
        /// <summary>แผนกที่ไม่มีอยู่จริง ใช้ตอนต้อง "ไม่ให้เห็นอะไรเลย" (fail closed)</summary>
        public const int NoDepartment = -999;

        /// <summary>
        /// แผนกทั้งหมดที่ user คนนี้ดูได้
        ///   null        = ดูได้ทุกแผนก (Admin)
        ///   อาร์เรย์ว่าง = ไม่ได้ผูกแผนกไว้เลย -> ไม่ให้เห็นอะไร
        ///
        /// Manager ดูได้เฉพาะแผนกที่ผูกไว้ใน meta.UserDepartment
        /// ระบบมีแค่สอง role แล้ว: Admin (ทุกแผนก) กับ Manager (เฉพาะที่ได้รับมอบหมาย)
        /// </summary>
        public static int[] GetAllowedDepartmentIds(IPrincipal user)
        {
            if (user == null || !user.Identity.IsAuthenticated)
                return new int[0];

            if (user.IsInRole("Admin"))
                return null;

            string userId = user.Identity.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return new int[0];

            /* หน้าหนึ่งถามสิทธิ์หลายรอบ (controller + view + repository)
               เก็บผลไว้ต่อ 1 request จะได้ไม่ยิง query ซ้ำ
               อายุแค่ request เดียว สิทธิ์ที่ถูกแก้จึงมีผลทันทีในหน้าถัดไป */
            var items = HttpContext.Current != null ? HttpContext.Current.Items : null;
            string cacheKey = "UserContext.Departments." + userId;

            if (items != null && items.Contains(cacheKey))
                return (int[])items[cacheKey];

            string connStr = ConfigurationManager.ConnectionStrings["KpiDb"].ConnectionString;
            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();
                var ids = conn.Query<int>(@"
                    SELECT ud.DepartmentId
                    FROM meta.UserDepartment ud
                    /* อ่านผ่าน rpt.vw_Department เพราะ db_kpi_web มีสิทธิ์ SELECT
                       เฉพาะ schema rpt/meta ไม่ได้แตะ core โดยตรง */
                    JOIN rpt.vw_Department d ON d.DepartmentId = ud.DepartmentId
                    WHERE ud.UserId = @UserId
                    ORDER BY ud.IsPrimary DESC, ud.DepartmentId",
                    new { UserId = userId });

                int[] result = ids.ToArray();
                if (items != null) items[cacheKey] = result;
                return result;
            }
        }

        /// <summary>
        /// รายการ DepartmentId คั่นด้วย comma สำหรับส่งให้ proc ฝั่ง SQL
        /// null = ทุกแผนก | "-999" = ไม่ให้เห็นอะไรเลย
        /// </summary>
        public static string GetAllowedDepartmentIdCsv(IPrincipal user)
        {
            int[] ids = GetAllowedDepartmentIds(user);
            if (ids == null) return null;
            if (ids.Length == 0) return NoDepartment.ToString();
            return string.Join(",", ids);
        }

        /// <summary>
        /// คืนแผนกหลักของ user — ใช้กับหน้าจอที่ยังเลือกได้ทีละแผนก
        /// null = ดูได้ทุกแผนก
        /// </summary>
        public static int? GetAllowedDepartmentId(IPrincipal user)
        {
            int[] ids = GetAllowedDepartmentIds(user);
            if (ids == null) return null;
            return ids.Length > 0 ? ids[0] : NoDepartment;
        }

        /// <summary>Admin เท่านั้นที่เห็นได้ทุกแผนกโดยไม่ต้องผูก</summary>
        public static bool CanViewAllDepartments(IPrincipal user)
        {
            return user != null
                && user.Identity.IsAuthenticated
                && user.IsInRole("Admin");
        }

        /// <summary>
        /// ตรวจว่า user มีสิทธิ์ดูแผนกที่ร้องขอมาหรือไม่
        ///
        /// *** ต้องเรียกทุกครั้งที่รับ DepartmentId มาจากฝั่ง client ***
        /// Manager ที่ดูแลแผนก 1,3 แก้ URL เป็น ?dept=5 ต้องไม่ได้ข้อมูลแผนก 5
        /// </summary>
        public static bool IsDepartmentAllowed(IPrincipal user, int requestedDepartmentId)
        {
            int[] allowed = GetAllowedDepartmentIds(user);
            if (allowed == null)
                return true;

            return Array.IndexOf(allowed, requestedDepartmentId) >= 0;
        }

        /// <summary>
        /// กรองรายการแผนกที่ client ส่งมา ให้เหลือเฉพาะที่มีสิทธิ์จริง
        /// คืน null เมื่อไม่ได้ระบุมาและ user เห็นได้ทุกแผนก
        /// </summary>
        public static int[] FilterAllowedDepartments(IPrincipal user, int[] requested)
        {
            int[] allowed = GetAllowedDepartmentIds(user);

            if (requested == null || requested.Length == 0)
                return allowed;                       // null = ทุกแผนก

            if (allowed == null)
                return requested.Distinct().ToArray();

            return requested.Where(id => allowed.Contains(id)).Distinct().ToArray();
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
