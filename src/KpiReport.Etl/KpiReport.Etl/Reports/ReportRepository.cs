using System.Collections.Generic;
using System.Data.SqlClient;
using Dapper;

namespace KpiReport.Etl.Reports
{
    /// <summary>
    /// อ่านรายชื่อผู้รับ และบันทึกผลการส่งลง meta.ReportDeliveryLog
    ///
    /// ตาราง ReportDeliveryLog มีอยู่ในสคีมาตั้งแต่ต้นโครงการแล้ว
    /// (02_meta_tables.sql) งานนี้แค่เข้ามาใช้ตามที่ออกแบบไว้
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

        /// <summary>เดือนล่าสุดที่มีข้อมูลจริง — นิยามเดียวกับที่หน้า Dashboard ใช้</summary>
        public int? GetLatestMonthKey()
        {
            using (var conn = Open())
            {
                return conn.ExecuteScalar<int?>("SELECT MAX(MonthKey) FROM rpt.vw_ValidMonth");
            }
        }

        public List<ReportSubscription> GetActiveSubscriptions()
        {
            using (var conn = Open())
            {
                var rows = conn.Query<ReportSubscription>(@"
                    SELECT SubscriptionId, Email, DisplayName, DepartmentId, DepartmentName
                    FROM meta.vw_ActiveReportSubscription
                    ORDER BY CASE WHEN DepartmentId IS NULL THEN 0 ELSE 1 END, Email");

                return new List<ReportSubscription>(rows);
            }
        }

        /// <summary>
        /// เคยส่งฉบับนี้สำเร็จไปแล้วหรือยัง
        ///
        /// จำเป็นเพราะงานนี้ถูกเรียกจาก Task Scheduler ซึ่งอาจยิงซ้ำได้
        /// (เครื่อง restart, ตั้งตารางทับกัน, คนกดรันมือซ้ำ)
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

        /// <summary>
        /// จองแถว log ไว้ก่อนส่ง (Status = PENDING)
        ///
        /// เขียนก่อนส่งเสมอ ไม่ใช่หลังส่ง เพราะถ้าโปรเซสตายกลางทาง
        /// จะยังเหลือหลักฐานว่าพยายามส่งอะไรไป แถวที่ค้าง PENDING
        /// คือสัญญาณว่ามีบางอย่างล้มแบบไม่ทันได้บันทึก
        /// </summary>
        public long LogPending(int monthKey, string reportName, string fileFormat,
                               string recipients, long fileSizeBytes)
        {
            using (var conn = Open())
            {
                return conn.ExecuteScalar<long>(@"
                    INSERT INTO meta.ReportDeliveryLog
                        (MonthKey, ReportName, FileFormat, Recipients, FileSizeBytes, Status)
                    VALUES
                        (@MonthKey, @ReportName, @FileFormat, @Recipients, @FileSizeBytes, 'PENDING');
                    SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
                    new
                    {
                        MonthKey = monthKey,
                        ReportName = reportName,
                        FileFormat = fileFormat,
                        Recipients = recipients,
                        FileSizeBytes = fileSizeBytes
                    });
            }
        }

        public void MarkSent(long deliveryId)
        {
            using (var conn = Open())
            {
                conn.Execute(@"
                    UPDATE meta.ReportDeliveryLog
                    SET Status = 'SENT', SentAt = SYSDATETIME(), ErrorMessage = NULL
                    WHERE DeliveryId = @DeliveryId",
                    new { DeliveryId = deliveryId });
            }
        }

        public void MarkFailed(long deliveryId, string errorMessage)
        {
            using (var conn = Open())
            {
                conn.Execute(@"
                    UPDATE meta.ReportDeliveryLog
                    SET Status       = 'FAILED',
                        ErrorMessage = @ErrorMessage,
                        RetryCount   = RetryCount + 1
                    WHERE DeliveryId = @DeliveryId",
                    new { DeliveryId = deliveryId, ErrorMessage = errorMessage });
            }
        }
    }
}
