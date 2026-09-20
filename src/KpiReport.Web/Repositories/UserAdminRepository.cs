using System.Collections.Generic;
using System.Linq;
using System.Data.SqlClient;
using Dapper;
using KpiReport.Web.Models;

namespace KpiReport.Web.Repositories
{
    /// <summary>
    /// ส่วนของข้อมูลผู้ใช้ที่อยู่ฝั่ง KPI ไม่ใช่ฝั่ง ASP.NET Identity
    ///
    /// การแบ่งงาน:
    ///   Identity (UserManager/RoleManager) -> บัญชี รหัสผ่าน role
    ///   ที่นี่                              -> meta.UserDepartment ว่า user ไหนดูแผนกไหน
    ///
    /// แยกกันแบบนี้เพื่อไม่ต้องเขียน SQL ยุ่งกับตาราง AspNet* เอง
    /// (ปล่อยให้ Identity จัดการ hash รหัสผ่านและ security stamp ของมันไป)
    /// </summary>
    public class UserAdminRepository
    {
        private readonly string _connectionString;

        public UserAdminRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        private SqlConnection Open()
        {
            var conn = new SqlConnection(_connectionString);
            conn.Open();
            return conn;
        }

        /// <summary>
        /// รายชื่อแผนกจริงสำหรับ dropdown — ตัดแถว -99 ("ทุกแผนก") ออก
        /// เพราะการผูก Manager เข้ากับแถว "ทุกแผนก" ไม่มีความหมาย
        /// ถ้าอยากให้ใครเห็นทุกแผนกให้ตั้ง role เป็น Admin แทน
        /// </summary>
        public List<DepartmentOption> GetRealDepartments()
        {
            const string sql = @"
                SELECT DepartmentId, DepartmentName
                FROM rpt.vw_Department
                WHERE DepartmentId <> -99
                ORDER BY DepartmentName";

            using (var conn = Open())
            {
                return new List<DepartmentOption>(conn.Query<DepartmentOption>(sql));
            }
        }

        /// <summary>
        /// แผนกของทุก user ในครั้งเดียว (UserId -> ชื่อ/รหัสแผนก)
        /// ดึงทีเดียวแล้ว join ในหน่วยความจำ ดีกว่ายิง query ต่อ 1 แถวในตาราง
        /// </summary>
        public Dictionary<string, DepartmentOption> GetDepartmentByUser()
        {
            const string sql = @"
                SELECT ud.UserId, d.DepartmentId, d.DepartmentName
                FROM meta.UserDepartment ud
                JOIN rpt.vw_Department d ON d.DepartmentId = ud.DepartmentId
                WHERE ud.IsPrimary = 1";

            var map = new Dictionary<string, DepartmentOption>();

            using (var conn = Open())
            {
                foreach (var row in conn.Query(sql))
                {
                    string userId = (string)row.UserId;
                    if (!map.ContainsKey(userId))
                    {
                        map[userId] = new DepartmentOption
                        {
                            DepartmentId = (int)row.DepartmentId,
                            DepartmentName = (string)row.DepartmentName
                        };
                    }
                }
            }

            return map;
        }

        public int? GetDepartmentId(string userId)
        {
            using (var conn = Open())
            {
                return conn.QueryFirstOrDefault<int?>(@"
                    SELECT TOP 1 DepartmentId
                    FROM meta.UserDepartment
                    WHERE UserId = @UserId
                    ORDER BY IsPrimary DESC, DepartmentId",
                    new { UserId = userId });
            }
        }

        /// <summary>
        /// ตั้งแผนกของ user ให้เป็นชุดที่ส่งมา (ว่าง/null = ไม่ผูกแผนกเลย)
        ///
        /// Manager หนึ่งคนดูแลได้หลายแผนก จึงรับเป็นรายการ
        /// ลบของเดิมทิ้งก่อนเสมอ เพื่อกันไม่ให้เหลือ mapping เก่าค้าง
        /// เช่นย้าย Manager จาก PROD1 ไป PROD2 แล้วยังเห็น PROD1 ได้อยู่
        ///
        /// แผนกแรกในรายการถูกตั้งเป็น IsPrimary = 1 ใช้เป็นค่าเริ่มต้นของ
        /// หน้าจอที่ยังเลือกได้ทีละแผนก
        /// </summary>
        public void SetDepartments(string userId, int[] departmentIds)
        {
            using (var conn = Open())
            using (var tx = conn.BeginTransaction())
            {
                conn.Execute(
                    "DELETE FROM meta.UserDepartment WHERE UserId = @UserId",
                    new { UserId = userId }, tx);

                if (departmentIds != null)
                {
                    bool first = true;
                    foreach (int deptId in departmentIds.Distinct())
                    {
                        conn.Execute(@"
                            INSERT INTO meta.UserDepartment (UserId, DepartmentId, IsPrimary)
                            VALUES (@UserId, @DeptId, @IsPrimary)",
                            new { UserId = userId, DeptId = deptId, IsPrimary = first }, tx);
                        first = false;
                    }
                }

                tx.Commit();
            }
        }

        /// <summary>แผนกทั้งหมดของ user หนึ่งคน (ว่าง = ไม่ผูกแผนกเลย)</summary>
        public List<int> GetDepartmentIds(string userId)
        {
            using (var conn = Open())
            {
                return new List<int>(conn.Query<int>(@"
                    SELECT DepartmentId FROM meta.UserDepartment
                    WHERE UserId = @UserId
                    ORDER BY IsPrimary DESC, DepartmentId",
                    new { UserId = userId }));
            }
        }

        /// <summary>
        /// แผนกทั้งหมดของทุก user ในครั้งเดียว (UserId -> รายการแผนก)
        /// ดึงทีเดียวแล้ว join ในหน่วยความจำ ดีกว่ายิง query ต่อ 1 แถวในตาราง
        /// </summary>
        public Dictionary<string, List<DepartmentOption>> GetDepartmentsByUser()
        {
            const string sql = @"
                SELECT ud.UserId, d.DepartmentId, d.DepartmentName
                FROM meta.UserDepartment ud
                JOIN rpt.vw_Department d ON d.DepartmentId = ud.DepartmentId
                ORDER BY ud.UserId, ud.IsPrimary DESC, d.DepartmentName";

            var map = new Dictionary<string, List<DepartmentOption>>();

            using (var conn = Open())
            {
                foreach (var row in conn.Query(sql))
                {
                    string userId = (string)row.UserId;
                    List<DepartmentOption> list;
                    if (!map.TryGetValue(userId, out list))
                    {
                        list = new List<DepartmentOption>();
                        map[userId] = list;
                    }

                    list.Add(new DepartmentOption
                    {
                        DepartmentId = (int)row.DepartmentId,
                        DepartmentName = (string)row.DepartmentName
                    });
                }
            }

            return map;
        }

        // ---------------------------------------------------------------
        // ธงบังคับเปลี่ยนรหัสผ่าน (meta.UserSecurity)
        // ---------------------------------------------------------------

        /// <summary>
        /// ตั้ง/ปลดธง "ต้องเปลี่ยนรหัสผ่านก่อนใช้งาน"
        ///
        /// ตั้งเป็น true ทุกครั้งที่ Admin เป็นคนกำหนดรหัสให้ (สร้างบัญชี / reset)
        /// ปลดเป็น false เมื่อเจ้าตัวเปลี่ยนรหัสด้วยตัวเองสำเร็จ
        ///
        /// ผลคือรหัสที่ Admin รู้ใช้เข้าระบบได้ครั้งเดียว จากนั้นมีแต่เจ้าของบัญชี
        /// ที่รู้รหัสจริง — audit log จึงยังชี้ตัวคนทำได้อย่างมีน้ำหนัก
        /// </summary>
        public void SetMustChangePassword(string userId, bool value, string setByUserName)
        {
            const string sql = @"
                MERGE meta.UserSecurity AS target
                USING (SELECT @UserId AS UserId) AS source
                    ON target.UserId = source.UserId
                WHEN MATCHED THEN
                    UPDATE SET MustChangePassword = @Value,
                               SetByUserName      = @SetBy,
                               UpdatedAt          = SYSDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (UserId, MustChangePassword, SetByUserName)
                    VALUES (@UserId, @Value, @SetBy);";

            using (var conn = Open())
            {
                conn.Execute(sql, new { UserId = userId, Value = value, SetBy = setByUserName });
            }
        }

        /// <summary>
        /// ถูกเรียกทุก request ของผู้ใช้ที่ login อยู่ (ผ่าน RequirePasswordChangeFilter)
        /// จึงตั้งใจให้เป็น query เล็กที่สุด: อ่านค่าเดียวด้วย primary key
        ///
        /// ทางเลือกคือฝังไว้ใน claim ตอน sign in จะไม่ต้องแตะฐานข้อมูลเลย
        /// แต่ต้องระวังเรื่อง claim ค้างเมื่อ Admin ตั้งธงตอนผู้ใช้ยัง login อยู่
        /// สำหรับระบบภายในขนาดนี้ อ่านตรง ๆ ตรงไปตรงมากว่าและถูกต้องเสมอ
        /// </summary>
        public bool MustChangePassword(string userId)
        {
            using (var conn = Open())
            {
                return conn.QueryFirstOrDefault<bool>(@"
                    SELECT MustChangePassword
                    FROM meta.UserSecurity
                    WHERE UserId = @UserId",
                    new { UserId = userId });
            }
        }

        /// <summary>UserId ทั้งหมดที่ยังติดธงอยู่ — ใช้ทำเครื่องหมายในตารางรายชื่อ</summary>
        public HashSet<string> GetMustChangePasswordUserIds()
        {
            using (var conn = Open())
            {
                var ids = conn.Query<string>(@"
                    SELECT UserId
                    FROM meta.UserSecurity
                    WHERE MustChangePassword = 1");

                return new HashSet<string>(ids);
            }
        }
    }
}
