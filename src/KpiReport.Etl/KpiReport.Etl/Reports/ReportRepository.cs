using System.Collections.Generic;
using System.Data.SqlClient;
using Dapper;

namespace KpiReport.Etl.Reports
{
    /// <summary>
    /// อ่านรายชื่อผู้รับของรอบส่งอัตโนมัติ และตรวจว่าเคยส่งไปแล้วหรือยัง
    ///
    /// การเขียน log ลง meta.ReportDeliveryLog ย้ายไปอยู่ที่
    /// ReportDeliveryRepository ในโปรเจกต์เว็บ (ผูกเข้ามาด้วย csproj Link)
    /// เพราะปุ่ม "ส่งเดี๋ยวนี้" ในหน้าผู้รับรายงานต้องเขียน log ชุดเดียวกัน
    /// </summary>
    public class ReportRepository
    {
        private readonly string _connectionString;

        public ReportRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        private SqlConnection Open()
        {
            var conn = new SqlConnection(_connectionString);
            conn.Open();
            return conn;
        }

        public List<ReportSubscription> GetActiveSubscriptions()
        {
            using (var conn = Open())
            {
                var rows = conn.Query<ReportSubscription>(@"
                    SELECT SubscriptionId, Email, DisplayName,
                           DepartmentIds, DepartmentNames, DepartmentCount,
                           SendDayOfMonth, SendHour
                    FROM meta.vw_ActiveReportSubscription
                    ORDER BY CASE WHEN DepartmentCount = 0 THEN 0 ELSE 1 END, Email");

                return new List<ReportSubscription>(rows);
            }
        }

        /// <summary>
        /// เคยส่งฉบับนี้สำเร็จไปแล้วหรือยัง
        ///
        /// จำเป็นเพราะงานนี้ถูกเรียกจาก Task Scheduler ซึ่งอาจยิงซ้ำได้
        /// (เครื่อง restart, ตั้งตารางทับกัน, คนกดรันมือซ้ำ) และเพราะผู้ดูแล
        /// กด "ส่งเดี๋ยวนี้" จากหน้าเว็บได้ด้วย
        /// การส่งรายงานเดือนเดียวกันไปหาคนเดิมสองรอบดูไม่เป็นมืออาชีพ
        /// ข้ามด้วยการเช็คนี้ ถ้าอยากส่งซ้ำจริง ๆ ให้ใช้ --force
        /// </summary>
        public bool AlreadySent(int monthKey, string reportName, string email)
        {
            using (var conn = Open())
            {
                return conn.ExecuteScalar<int>(@"
                    SELECT COUNT(1)
                    FROM meta.ReportDeliveryLog
                    WHERE MonthKey   = @MonthKey
                      AND ReportName = @ReportName
                      AND Recipients = @Email
                      AND Status     = 'SENT'",
                    new { MonthKey = monthKey, ReportName = reportName, Email = email }) > 0;
            }
        }
    }
}
