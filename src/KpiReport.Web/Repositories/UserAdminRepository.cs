using System.Collections.Generic;
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
        /// เพราะการผูก Viewer เข้ากับ "ทุกแผนก" ไม่มีความหมาย
        /// ถ้าอยากให้ใครเห็นทุกแผนกให้ตั้ง role เป็น Manager แทน
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
        /// ตั้งแผนกของ user ให้เป็นค่าที่ส่งมา (null = ไม่ผูกแผนกเลย)
        ///
        /// ลบของเดิมทิ้งก่อนเสมอ เพื่อกันไม่ให้เหลือ mapping เก่าค้าง
        /// เช่นย้าย Viewer จาก LINE_A ไป LINE_B แล้วยังเห็น LINE_A ได้อยู่
        /// </summary>
        public void SetDepartment(string userId, int? departmentId)
        {
            using (var conn = Open())
            using (var tx = conn.BeginTransaction())
            {
                conn.Execute(
                    "DELETE FROM meta.UserDepartment WHERE UserId = @UserId",
                    new { UserId = userId }, tx);

                if (departmentId.HasValue)
                {
                    conn.Execute(@"
                        INSERT INTO meta.UserDepartment (UserId, DepartmentId, IsPrimary)
                        VALUES (@UserId, @DeptId, 1)",
                        new { UserId = userId, DeptId = departmentId.Value }, tx);
                }

                tx.Commit();
            }
        }
    }
}
