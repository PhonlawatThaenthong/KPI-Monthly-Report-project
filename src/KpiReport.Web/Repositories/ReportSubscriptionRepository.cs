using System.Collections.Generic;
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
                       DepartmentId, DepartmentName, IsActive,
                       SendDayOfMonth, SendHour,
                       IsLinkedToUser, LinkedUserMissing, LinkedUserDisabled
                FROM meta.vw_ReportSubscriptionAdmin
                ORDER BY CASE WHEN DepartmentId IS NULL THEN 0 ELSE 1 END,
                         DepartmentName, Email";

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
        public bool Add(string userId, string email, string displayName, int? departmentId,
                        byte sendDayOfMonth, byte sendHour)
        {
            const string sql = @"
                INSERT INTO meta.ReportSubscription
                    (UserId, Email, DisplayName, DepartmentId, SendDayOfMonth, SendHour)
                VALUES
                    (@UserId, @Email, @DisplayName, @DepartmentId, @SendDayOfMonth, @SendHour)";

            using (var conn = Open())
            {
                try
                {
                    conn.Execute(sql, new
                    {
                        UserId = userId,
                        Email = email,
                        DisplayName = displayName,
                        DepartmentId = departmentId,
                        SendDayOfMonth = sendDayOfMonth,
                        SendHour = sendHour
                    });
                    return true;
                }
                catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
                {
                    // 2601/2627 = ชนกับ unique index
                    return false;
                }
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
                           DepartmentId, DepartmentName, IsActive,
                           SendDayOfMonth, SendHour,
                           IsLinkedToUser, LinkedUserMissing, LinkedUserDisabled
                    FROM meta.vw_ReportSubscriptionAdmin
                    WHERE SubscriptionId = @Id",
                    new { Id = subscriptionId });
            }
        }
    }
}
