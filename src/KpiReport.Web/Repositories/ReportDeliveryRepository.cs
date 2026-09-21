using System.Collections.Generic;
using System.Data.SqlClient;
using Dapper;
using KpiReport.Web.Models;

namespace KpiReport.Web.Repositories
{
    /// <summary>
    /// บันทึกผลการส่งรายงานลง meta.ReportDeliveryLog
    ///
    /// อยู่ในโปรเจกต์เว็บแต่ถูกผูกเข้า ETL ด้วย csproj Link เหมือน
    /// KpiRepository และ PdfReportBuilder — ทั้งงานส่งอัตโนมัติรายเดือน (ETL)
    /// และปุ่ม "ส่งเดี๋ยวนี้" ในหน้าผู้รับรายงาน (เว็บ) จึงเขียน log
    /// ด้วยตรรกะชุดเดียวกัน
    /// </summary>
    public class ReportDeliveryRepository
    {
        private readonly string _connectionString;

        public ReportDeliveryRepository(string connectionString)
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

        /// <summary>
        /// จองแถว log ไว้ก่อนส่ง (Status = PENDING)
        ///
        /// เขียนก่อนส่งเสมอ ไม่ใช่หลังส่ง เพราะถ้าโปรเซสตายกลางทาง
        /// จะยังเหลือหลักฐานว่าพยายามส่งอะไรไป แถวที่ค้าง PENDING
        /// คือสัญญาณว่ามีบางอย่างล้มแบบไม่ทันได้บันทึก
        ///
        /// subscriptionId / triggerType / triggeredBy / scopeLabel คือสิ่งที่
        /// ทำให้หน้า log ตอบได้ว่า "ใครกดส่งให้ใคร ขอบเขตไหน" — ScopeLabel
        /// เก็บเป็นข้อความ ณ เวลาส่ง ไม่ join กลับไปที่ subscription ตอนแสดงผล
        /// เพราะขอบเขตถูกแก้ภายหลังได้ แล้ว log จะเล่าเรื่องผิด
        /// </summary>
        public long LogPending(int monthKey, string reportName, string fileFormat,
                               string recipients, long fileSizeBytes,
                               int? subscriptionId = null, string triggerType = "SCHEDULED",
                               string triggeredBy = null, string scopeLabel = null)
        {
            using (var conn = Open())
            {
                return conn.ExecuteScalar<long>(@"
                    INSERT INTO meta.ReportDeliveryLog
                        (MonthKey, ReportName, FileFormat, Recipients, FileSizeBytes, Status,
                         SubscriptionId, TriggerType, TriggeredBy, ScopeLabel)
                    VALUES
                        (@MonthKey, @ReportName, @FileFormat, @Recipients, @FileSizeBytes, 'PENDING',
                         @SubscriptionId, @TriggerType, @TriggeredBy, @ScopeLabel);
                    SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
                    new
                    {
                        MonthKey = monthKey,
                        ReportName = reportName,
                        FileFormat = fileFormat,
                        Recipients = recipients,
                        FileSizeBytes = fileSizeBytes,
                        SubscriptionId = subscriptionId,
                        TriggerType = triggerType == "MANUAL" ? "MANUAL" : "SCHEDULED",
                        TriggeredBy = triggeredBy,
                        ScopeLabel = scopeLabel
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

        // ---------------------------------------------------------------
        // อ่าน log สำหรับหน้าจอ
        //
        // ทุก query อ่านผ่าน meta.vw_ReportDeliveryLog ตัวเดียว หน้า log
        // ต่อผู้รับกับหน้ารวมทุกการส่งจึงนิยาม "1 ฉบับ" เหมือนกันเป๊ะ
        // ---------------------------------------------------------------

        private const string LogColumns = @"
            DeliveryId, SubscriptionId, MonthKey, ReportName, FileFormat, FileSizeBytes,
            Email, DisplayName, ScopeLabel, TriggerType, TriggeredBy,
            Status, ErrorMessage, RetryCount, CreatedAt, SentAt";

        /// <summary>
        /// log ของผู้รับ 1 ราย ใหม่ไปเก่า
        ///
        /// ดึงด้วย SubscriptionId เป็นหลัก แต่รวมแถวที่อีเมลตรงกันด้วย
        /// เพราะแถวที่เขียนไว้ก่อนมี migration 34 ยังไม่มี SubscriptionId
        /// ผูกไว้ — ถ้าไม่รวม ประวัติเก่าของผู้รับรายนั้นจะหายไปจากหน้าจอ
        /// </summary>
        public List<ReportDeliveryLogRow> GetBySubscription(int subscriptionId, string email, int take)
        {
            using (var conn = Open())
            {
                var rows = conn.Query<ReportDeliveryLogRow>(@"
                    SELECT TOP (@Take) " + LogColumns + @"
                    FROM meta.vw_ReportDeliveryLog
                    WHERE SubscriptionId = @SubscriptionId
                       OR (SubscriptionId IS NULL AND @Email IS NOT NULL AND Email = @Email)
                    ORDER BY DeliveryId DESC",
                    new { SubscriptionId = subscriptionId, Email = email, Take = take });

                return new List<ReportDeliveryLogRow>(rows);
            }
        }

        /// <summary>
        /// log ทุกการส่ง กรองได้ตามเดือน / สถานะ / ที่มา
        ///
        /// allowedEmails = null คือไม่จำกัด (Admin) ส่วน Manager ได้รับ
        /// รายชื่ออีเมลของผู้รับที่ตัวเองดูแลมาเท่านั้น — กรองที่ SQL
        /// ไม่ใช่หลังดึงออกมา ไม่งั้น TOP จะนับแถวที่เขาไม่มีสิทธิ์เห็นด้วย
        /// </summary>
        public List<ReportDeliveryLogRow> Search(int? monthKey, string status, string triggerType,
                                                 IEnumerable<string> allowedEmails, int take)
        {
            List<string> emails = allowedEmails == null ? null : new List<string>(allowedEmails);

            // Manager ที่ยังไม่มีผู้รับในความดูแลเลย ไม่ต้องยิง query
            if (emails != null && emails.Count == 0) return new List<ReportDeliveryLogRow>();

            using (var conn = Open())
            {
                var rows = conn.Query<ReportDeliveryLogRow>(@"
                    SELECT TOP (@Take) " + LogColumns + @"
                    FROM meta.vw_ReportDeliveryLog
                    WHERE (@MonthKey    IS NULL OR MonthKey    = @MonthKey)
                      AND (@Status      IS NULL OR Status      = @Status)
                      AND (@TriggerType IS NULL OR TriggerType = @TriggerType)
                      AND (@FilterEmails = 0 OR Email IN @Emails)
                    ORDER BY DeliveryId DESC",
                    new
                    {
                        MonthKey = monthKey,
                        Status = status,
                        TriggerType = triggerType,
                        FilterEmails = emails == null ? 0 : 1,
                        Emails = emails ?? new List<string> { string.Empty },
                        Take = take
                    });

                return new List<ReportDeliveryLogRow>(rows);
            }
        }

        /// <summary>เดือนที่มี log อยู่จริง ใช้เติม dropdown ตัวกรอง</summary>
        public List<int> GetLoggedMonths()
        {
            using (var conn = Open())
            {
                var rows = conn.Query<int>(@"
                    SELECT DISTINCT MonthKey
                    FROM meta.ReportDeliveryLog
                    ORDER BY MonthKey DESC");

                return new List<int>(rows);
            }
        }
    }
}
