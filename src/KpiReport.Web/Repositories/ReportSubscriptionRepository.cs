using System.Collections.Generic;
using System.Linq;
using System.Data.SqlClient;
using Dapper;
using KpiReport.Web.Models;

namespace KpiReport.Web.Repositories
{
    /// <summary>
    /// จัดการรายชื่อผู้รับรายงานทางอีเมล (meta.ReportSubscription)
    ///
    /// ผู้รับมี 2 แบบ:
    ///   ผูกกับบัญชีในระบบ -> เก็บแค่ UserId อีเมลดึงจากบัญชีตอนอ่าน
    ///                        บัญชีถูกปิด/ลบ = หยุดส่งเอง ไม่ต้อง sync
    ///   อีเมลภายนอก      -> เก็บ Email ตรง ๆ สำหรับคนที่ไม่มีบัญชี
    /// </summary>
    public class ReportSubscriptionRepository
    {
        private readonly string _connectionString;

        public ReportSubscriptionRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        private SqlConnection Open()
        {
            var conn = new SqlConnection(_connectionString);
            conn.Open();
            return conn;
        }

        public List<ReportSubscriptionRow> GetAll()
        {
            const string sql = @"
                SELECT SubscriptionId, UserId, Email, DisplayName,
                       DepartmentIds, DepartmentNames, DepartmentCount, IsActive,
                       SendDayOfMonth, SendHour,
                       IsLinkedToUser, LinkedUserMissing, LinkedUserDisabled
                FROM meta.vw_ReportSubscriptionAdmin
                ORDER BY CASE WHEN DepartmentCount = 0 THEN 0 ELSE 1 END,
                         DepartmentNames, Email";

            using (var conn = Open())
            {
                return new List<ReportSubscriptionRow>(conn.Query<ReportSubscriptionRow>(sql));
            }
        }

        /// <summary>UserId ที่เป็นผู้รับอยู่แล้ว ใช้กรองออกจาก dropdown</summary>
        public HashSet<string> GetSubscribedUserIds()
        {
            using (var conn = Open())
            {
                var ids = conn.Query<string>(@"
                    SELECT UserId FROM meta.ReportSubscription WHERE UserId IS NOT NULL");
                return new HashSet<string>(ids);
            }
        }

        /// <summary>
        /// เพิ่มผู้รับ — ส่ง userId หรือ email อย่างใดอย่างหนึ่ง
        /// คืน false เมื่อซ้ำกับที่มีอยู่แล้ว (unique index เป็นคนตัดสิน
        /// ไม่ใช่การเช็คก่อน insert ซึ่งมีช่องว่างให้แทรกได้ระหว่างทาง)
        /// </summary>
        public bool Add(string userId, string email, string displayName, int[] departmentIds,
                        byte sendDayOfMonth, byte sendHour)
        {
            const string sql = @"
                INSERT INTO meta.ReportSubscription
                    (UserId, Email, DisplayName, SendDayOfMonth, SendHour)
                VALUES
                    (@UserId, @Email, @DisplayName, @SendDayOfMonth, @SendHour);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            using (var conn = Open())
            using (var tran = conn.BeginTransaction())
            {
                try
                {
                    int subscriptionId = conn.ExecuteScalar<int>(sql, new
                    {
                        UserId = userId,
                        Email = email,
                        DisplayName = displayName,
                        SendDayOfMonth = sendDayOfMonth,
                        SendHour = sendHour
                    }, tran);

                    InsertDepartments(conn, tran, subscriptionId, departmentIds);

                    tran.Commit();
                    return true;
                }
                catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
                {
                    // 2601/2627 = ชนกับ unique index (ผู้รับรายนี้มีอยู่แล้ว)
                    tran.Rollback();
                    return false;
                }
                catch
                {
                    tran.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// เปลี่ยนขอบเขตแผนกของผู้รับรายนี้ทั้งชุด
        ///
        /// ลบของเดิมแล้วใส่ใหม่ในทรานแซกชันเดียว ไม่ทำ diff ทีละแถว
        /// เพราะชุดนี้เล็กมาก (ไม่เกินจำนวนแผนกทั้งบริษัท) และการลบ-ใส่ใหม่
        /// ทำให้ไม่มีสถานะกลางที่ขอบเขตผิดชั่วคราว
        ///
        /// รายการว่าง = ทุกแผนก (ไม่มีแถวลูก)
        /// </summary>
        public void SetDepartments(int subscriptionId, int[] departmentIds)
        {
            using (var conn = Open())
            using (var tran = conn.BeginTransaction())
            {
                try
                {
                    conn.Execute(
                        "DELETE FROM meta.ReportSubscriptionDepartment WHERE SubscriptionId = @Id",
                        new { Id = subscriptionId }, tran);

                    InsertDepartments(conn, tran, subscriptionId, departmentIds);
                    tran.Commit();
                }
                catch
                {
                    tran.Rollback();
                    throw;
                }
            }
        }

        private static void InsertDepartments(SqlConnection conn, SqlTransaction tran,
                                              int subscriptionId, int[] departmentIds)
        {
            if (departmentIds == null || departmentIds.Length == 0)
                return;   // ไม่มีแถวลูก = ทุกแผนก

            foreach (int deptId in departmentIds.Distinct())
            {
                conn.Execute(@"
                    INSERT INTO meta.ReportSubscriptionDepartment (SubscriptionId, DepartmentId)
                    VALUES (@SubscriptionId, @DepartmentId)",
                    new { SubscriptionId = subscriptionId, DepartmentId = deptId }, tran);
            }
        }

        /// <summary>
        /// เปลี่ยนตารางเวลาส่งของผู้รับรายนี้
        /// ค่าที่เกินขอบเขตถูกดักที่ CHECK constraint ในฐานข้อมูลอีกชั้น
        /// แม้ผู้เรียกจะ validate มาแล้ว — กันการเรียกจากที่อื่นในอนาคต
        /// </summary>
        public void SetSchedule(int subscriptionId, byte sendDayOfMonth, byte sendHour)
        {
            using (var conn = Open())
            {
                conn.Execute(@"
                    UPDATE meta.ReportSubscription
                    SET SendDayOfMonth = @Day, SendHour = @Hour
                    WHERE SubscriptionId = @Id",
                    new { Id = subscriptionId, Day = sendDayOfMonth, Hour = sendHour });
            }
        }

        public void SetActive(int subscriptionId, bool isActive)
        {
            using (var conn = Open())
            {
                conn.Execute(@"
                    UPDATE meta.ReportSubscription
                    SET IsActive = @IsActive
                    WHERE SubscriptionId = @Id",
                    new { Id = subscriptionId, IsActive = isActive });
            }
        }

        public void Delete(int subscriptionId)
        {
            using (var conn = Open())
            {
                conn.Execute(
                    "DELETE FROM meta.ReportSubscription WHERE SubscriptionId = @Id",
                    new { Id = subscriptionId });
            }
        }

        public ReportSubscriptionRow GetById(int subscriptionId)
        {
            using (var conn = Open())
            {
                return conn.QueryFirstOrDefault<ReportSubscriptionRow>(@"
                    SELECT SubscriptionId, UserId, Email, DisplayName,
                           DepartmentIds, DepartmentNames, DepartmentCount, IsActive,
                           SendDayOfMonth, SendHour,
                           IsLinkedToUser, LinkedUserMissing, LinkedUserDisabled
                    FROM meta.vw_ReportSubscriptionAdmin
                    WHERE SubscriptionId = @Id",
                    new { Id = subscriptionId });
            }
        }
    }
}
