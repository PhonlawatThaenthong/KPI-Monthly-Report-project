using System.Data.SqlClient;
using Dapper;

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
