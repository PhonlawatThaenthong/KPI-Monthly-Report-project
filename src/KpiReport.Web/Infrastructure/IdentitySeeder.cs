using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Cryptography;
using Dapper;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using KpiReport.Web.Models;   // ApplicationDbContext, ApplicationUser (สร้างโดย template)

namespace KpiReport.Web.Infrastructure
{
    /// <summary>
    /// สร้าง Role, Admin คนแรกของระบบจริง, และบัญชีตัวอย่างตอนแอปเริ่มทำงาน
    ///
    /// แบ่งเป็น 2 ส่วนที่เป็นอิสระต่อกัน:
    ///
    ///   1) Admin คนแรก (production bootstrap) — ดู SeedInitialAdminIfNeeded()
    ///      คุมด้วย Auth:InitialAdminEmail เท่านั้น ทำงานได้ทั้ง Debug/Release
    ///      ไม่มีรหัสผ่านให้ leak เพราะไม่เคย generate ไว้ให้ใครรู้เลย
    ///      (สุ่มแล้วทิ้งทันที เข้าระบบครั้งแรกผ่านหน้า "ลืมรหัสผ่าน")
    ///
    ///   2) บัญชีตัวอย่างสำหรับ Development / เดโม เท่านั้น
    ///      คุมด้วยสองอย่าง:
    ///        Auth:SeedDemoUsers (ใน Web.config) — Release build ปิดเป็นค่าตั้งต้น
    ///        KPI_DEMO_PASSWORD (environment variable) — ไม่ตั้งค่าไว้ = ไม่ seed อะไรเลย
    ///      เดิมรหัสผ่านถูก hardcode ไว้ในไฟล์นี้ ซึ่งหลุดขึ้น GitHub ไปพร้อม source
    ///      ตอนนี้อ่านจาก environment variable แทน (ดู AuthSettings.DemoPassword)
    ///
    /// สร้าง Role เสมอ (ไม่ใช่ความลับ และระบบต้องมี Role ถึงจะทำงานได้)
    /// </summary>
    public static class IdentitySeeder
    {
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

                // ---------- 2) Admin คนแรกของระบบจริง ----------
                // เป็นอิสระจาก Auth:SeedDemoUsers — ทำงานได้ทั้ง Debug/Release
                // ทำเฉพาะตอนยังไม่มีใครอยู่ role Admin เลยเท่านั้น จึงปลอดภัย
                // ที่จะรันซ้ำทุกครั้งที่แอปสตาร์ท (deploy ใหม่/รีสตาร์ท IIS)
                SeedInitialAdminIfNeeded(context, roleManager, userManager);

                // ---------- 3) บัญชีตัวอย่างสำหรับ dev/demo ----------
                if (!AuthSettings.SeedDemoUsers)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[IdentitySeeder] ข้ามการสร้างบัญชีตัวอย่าง (Auth:SeedDemoUsers = false)");
                    return;
                }

                string demoPassword = AuthSettings.DemoPassword;
                if (string.IsNullOrWhiteSpace(demoPassword))
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[IdentitySeeder] ข้ามการสร้างบัญชีตัวอย่าง: ยังไม่ได้ตั้ง Auth:DemoPassword ใน Web.config");
                    return;
                }

                CreateUserIfMissing(userManager, demoPassword, "admin@kpi.local",   "Admin",   null);
                CreateUserIfMissing(userManager, demoPassword, "manager@kpi.local", "Manager", null);
                CreateUserIfMissing(userManager, demoPassword, "linea@kpi.local",   "Viewer",  "LINE_A");
                CreateUserIfMissing(userManager, demoPassword, "lineb@kpi.local",   "Viewer",  "LINE_B");
            }
        }

        /// <summary>
        /// สร้าง Admin คนแรกของระบบจริง ถ้ายังไม่มีใครอยู่ role Admin เลย
        ///
        /// ที่มา: ผู้ใช้ถามหาวิธี "build ใหม่มี user admin ได้โดยรหัสไม่หลุด"
        /// แนวทางนี้ (แทนที่จะ hardcode/env var รหัสผ่านตัวเดียวที่ทุกคนใช้ร่วมกัน)
        /// คือไม่สร้างรหัสผ่านที่ "มีคนรู้" ขึ้นมาเลยสักตัว:
        ///
        ///   1) สุ่มรหัสผ่านที่ปลอดภัย ใช้สร้างบัญชีแล้ว "ทิ้งทันที" ในหน่วยความจำ
        ///      ไม่ log ไม่เขียนไฟล์ ไม่ผ่าน config/env ที่ไหนเลย
        ///   2) ผู้ดูแลเข้าระบบครั้งแรกผ่านหน้า "ลืมรหัสผ่าน" ที่มีอยู่แล้ว
        ///      (/Account/ForgotPassword) โดยกรอกอีเมลนี้ ระบบจะส่งลิงก์ตั้ง
        ///      รหัสผ่านใหม่ให้ทาง SMTP ที่ตั้งไว้ — ได้ token ที่ปลอดภัยระดับ
        ///      เดียวกับที่ผู้ใช้ทั่วไปใช้ทุกวัน ไม่ต้องเขียน flow ใหม่แยกต่างหาก
        ///
        /// ทำไมไม่สร้าง token/ส่งอีเมลตรงนี้เลย:
        /// Application_Start ยังไม่มี OWIN/HTTP context ให้ Url.Action สร้าง
        /// callback URL และ token provider (DataProtectorTokenProvider ผูกกับ
        /// dataProtectionProvider ของ OWIN pipeline) ก็ยังไม่พร้อม ถ้าสร้าง
        /// token เองตรงนี้ด้วยคนละ provider จะเอาไปกดยืนยันหน้าเว็บจริงไม่ได้
        /// ปล่อยให้ผู้ดูแลกด "ลืมรหัสผ่าน" เองครั้งแรกจึงถูกต้องและปลอดภัยกว่า
        ///
        /// เช็คแค่ "มี Admin สักคนหรือยัง" ไม่ผูกกับอีเมลเจาะจง จึงรันซ้ำได้
        /// ทุกครั้งที่แอปสตาร์ทโดยไม่สร้างบัญชีซ้ำหรือรีเซ็ตรหัสของใคร
        /// </summary>
        private static void SeedInitialAdminIfNeeded(
            ApplicationDbContext context,
            RoleManager<IdentityRole> roleManager,
            UserManager<ApplicationUser> userManager)
        {
            string email = AuthSettings.InitialAdminEmail;
            if (string.IsNullOrWhiteSpace(email))
                return; // ไม่ได้ตั้งค่าไว้ = ปิดฟีเจอร์นี้

            var adminRole = roleManager.FindByName("Admin");
            if (adminRole == null)
                return; // ไม่ควรเกิด เพราะสร้าง Role ไปแล้วก่อนหน้านี้ในเมธอดนี้

            bool adminExists = context.Users.Any(u => u.Roles.Any(r => r.RoleId == adminRole.Id));
            if (adminExists)
                return; // มี Admin อยู่แล้ว — ไม่ยุ่งกับบัญชีที่มีอยู่

            var user = userManager.FindByName(email);
            if (user == null)
            {
                string throwawayPassword = GenerateRandomPassword();
                user = new ApplicationUser { UserName = email, Email = email };
                var result = userManager.Create(user, throwawayPassword);
                // throwawayPassword ไม่ถูกใช้อีกหลังจากนี้ — ไม่มีตัวแปรอื่นอ้างถึง
                // และไม่ log ค่านี้ไว้ที่ไหนเลยแม้ตอน error

                if (!result.Succeeded)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[IdentitySeeder] สร้าง Admin คนแรก ({email}) ไม่สำเร็จ: " +
                        string.Join(", ", result.Errors));
                    return;
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[IdentitySeeder] สร้าง Admin คนแรก ({email}) แล้ว — " +
                    "เข้าระบบครั้งแรกผ่านหน้า \"ลืมรหัสผ่าน\" เพื่อตั้งรหัสผ่านเอง");
            }

            if (!userManager.IsInRole(user.Id, "Admin"))
                userManager.AddToRole(user.Id, "Admin");
        }

        /// <summary>
        /// สุ่มรหัสผ่านที่ผ่านกฎความซับซ้อนแน่นอน (มีครบทุกหมวด) แล้วสับตำแหน่ง
        /// ใช้ครั้งเดียวตอนสร้างบัญชีแล้วทิ้ง ไม่มีใครอ่านค่านี้ได้อีกหลังจากนี้
        /// </summary>
        private static string GenerateRandomPassword()
        {
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string lower = "abcdefghijkmnopqrstuvwxyz";
            const string digit = "23456789";
            const string symbol = "!@#$%^&*-_=+";
            const string all = upper + lower + digit + symbol;

            using (var rng = new RNGCryptoServiceProvider())
            {
                var chars = new char[24];

                // การันตีว่ามีครบทุกหมวดตามที่ PasswordValidator ต้องการ
                chars[0] = PickRandomChar(rng, upper);
                chars[1] = PickRandomChar(rng, lower);
                chars[2] = PickRandomChar(rng, digit);
                chars[3] = PickRandomChar(rng, symbol);

                for (int i = 4; i < chars.Length; i++)
                    chars[i] = PickRandomChar(rng, all);

                // สับตำแหน่งด้วย Fisher-Yates โดยใช้ RNG ตัวเดียวกัน
                // ไม่ใช่ Random ปกติ เพราะ seed ของ Random เดาได้จากเวลาของเครื่อง
                for (int i = chars.Length - 1; i > 0; i--)
                {
                    int j = RandomInt(rng, i + 1);
                    var tmp = chars[i];
                    chars[i] = chars[j];
                    chars[j] = tmp;
                }

                return new string(chars);
            }
        }

        private static char PickRandomChar(RandomNumberGenerator rng, string alphabet)
        {
            return alphabet[RandomInt(rng, alphabet.Length)];
        }

        /// <summary>
        /// สุ่มจำนวนเต็ม [0, exclusiveMax) แบบไม่มี modulo bias
        /// </summary>
        private static int RandomInt(RandomNumberGenerator rng, int exclusiveMax)
        {
            var buffer = new byte[4];
            uint limit = uint.MaxValue - (uint.MaxValue % (uint)exclusiveMax);
            uint value;
            do
            {
                rng.GetBytes(buffer);
                value = BitConverter.ToUInt32(buffer, 0);
            } while (value >= limit);

            return (int)(value % (uint)exclusiveMax);
        }

        private static void CreateUserIfMissing(
            UserManager<ApplicationUser> userManager,
            string password,
            string email,
            string roleName,
            string departmentCode)
        {
            var user = userManager.FindByName(email);

            if (user == null)
            {
                user = new ApplicationUser { UserName = email, Email = email };
                var result = userManager.Create(user, password);

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
