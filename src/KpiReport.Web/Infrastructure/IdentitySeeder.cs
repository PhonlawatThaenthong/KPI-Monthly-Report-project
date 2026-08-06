using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using Dapper;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using KpiReport.Web.Models;   // ApplicationDbContext, ApplicationUser (สร้างโดย template)

namespace KpiReport.Web.Infrastructure
{
    /// <summary>
    /// สร้าง Role และ User ตั้งต้นตอนแอปเริ่มทำงานครั้งแรก
    ///
    /// *** สำหรับ Development เท่านั้น ***
    /// ก่อนใช้งานจริงต้อง:
    ///   1. เปลี่ยนรหัสผ่านทุกบัญชี
    ///   2. ลบการเรียก Seed() ออกจาก Global.asax หรือครอบด้วย #if DEBUG
    /// </summary>
    public static class IdentitySeeder
    {
        // รหัสผ่าน dev - ต้องเปลี่ยนก่อนส่งงานจริง
        private const string DemoPassword = "Passw0rd!2026";

        public static void Seed()
        {
            using (var context = new ApplicationDbContext())
            {
                var roleStore = new RoleStore<IdentityRole>(context);
                var roleManager = new RoleManager<IdentityRole>(roleStore);

                var userStore = new UserStore<ApplicationUser>(context);
                var userManager = new UserManager<ApplicationUser>(userStore);

                // ผ่อนกฎรหัสผ่านให้พอเหมาะกับ dev
                // งานจริงควรเข้มกว่านี้ และบังคับเปลี่ยนรหัสครั้งแรกที่ login
                userManager.PasswordValidator = new PasswordValidator
                {
                    RequiredLength = 8,
                    RequireNonLetterOrDigit = true,
                    RequireDigit = true,
                    RequireLowercase = true,
                    RequireUppercase = true
                };

                // ---------- 1) Roles ----------
                foreach (var roleName in new[] { "Admin", "Manager", "Viewer" })
                {
                    if (!roleManager.RoleExists(roleName))
                        roleManager.Create(new IdentityRole(roleName));
                }

                // ---------- 2) Users ----------
                CreateUserIfMissing(userManager, "admin@kpi.local",   "Admin",   null);
                CreateUserIfMissing(userManager, "manager@kpi.local", "Manager", null);
                CreateUserIfMissing(userManager, "linea@kpi.local",   "Viewer",  "LINE_A");
                CreateUserIfMissing(userManager, "lineb@kpi.local",   "Viewer",  "LINE_B");
            }
        }

        private static void CreateUserIfMissing(
            UserManager<ApplicationUser> userManager,
            string email,
            string roleName,
            string departmentCode)
        {
            var user = userManager.FindByName(email);

            if (user == null)
            {
                user = new ApplicationUser { UserName = email, Email = email };
                var result = userManager.Create(user, DemoPassword);

                if (!result.Succeeded)
                {
                    // ไม่ throw เพราะจะทำให้แอปเปิดไม่ขึ้นทั้งระบบ
                    // แต่ต้องเห็นใน Output window ว่าพลาดเพราะอะไร
                    System.Diagnostics.Debug.WriteLine(
                        $"[IdentitySeeder] สร้าง {email} ไม่สำเร็จ: " +
                        string.Join(", ", result.Errors));
                    return;
                }
            }

            if (!userManager.IsInRole(user.Id, roleName))
                userManager.AddToRole(user.Id, roleName);

            if (!string.IsNullOrEmpty(departmentCode))
                LinkUserToDepartment(user.Id, departmentCode);
        }

        /// <summary>
        /// ผูก user เข้ากับแผนกใน meta.UserDepartment
        /// ใช้ Dapper เพราะตารางนี้อยู่นอกโมเดลของ Entity Framework
        /// </summary>
        private static void LinkUserToDepartment(string userId, string departmentCode)
        {
            string connStr = ConfigurationManager.ConnectionStrings["KpiDb"].ConnectionString;

            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();

                int? deptId = conn.QueryFirstOrDefault<int?>(
                    "SELECT DepartmentId FROM core.DimDepartment WHERE DepartmentCode = @Code",
                    new { Code = departmentCode });

                if (deptId == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[IdentitySeeder] ไม่พบแผนกรหัส {departmentCode}");
                    return;
                }

                conn.Execute(@"
                    IF NOT EXISTS (
                        SELECT 1 FROM meta.UserDepartment
                        WHERE UserId = @UserId AND DepartmentId = @DeptId)
                    BEGIN
                        INSERT INTO meta.UserDepartment (UserId, DepartmentId, IsPrimary)
                        VALUES (@UserId, @DeptId, 1);
                    END",
                    new { UserId = userId, DeptId = deptId.Value });
            }
        }
    }
}
