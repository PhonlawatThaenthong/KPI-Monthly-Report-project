using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web;
using Dapper;

namespace KpiReport.Web.Infrastructure
{
    /// <summary>
    /// บันทึกการกระทำของผู้ใช้ลง meta.AuditLog
    ///
    /// สิ่งที่ควรบันทึก: LOGIN, LOGIN_FAILED, LOGOUT, EXPORT, KPI_EDIT, TARGET_EDIT
    /// สิ่งที่ไม่ควรบันทึก: การเปิดหน้าดูข้อมูลธรรมดา (จะทำให้ตารางบวมโดยไม่จำเป็น)
    ///
    /// การเขียน log ต้องไม่ทำให้ระบบหลักล่ม จึงกลืน exception ทิ้ง
    /// แต่ยังเขียนลง Debug output ให้เห็นตอนพัฒนา
    /// </summary>
    public static class AuditLogger
    {
        public static void Write(
            string actionType,
            string userId = null,
            string userName = null,
            string entityName = null,
            string entityKey = null,
            string detail = null,
            bool isSuccess = true)
        {
            try
            {
                string connStr = ConfigurationManager.ConnectionStrings["KpiDb"].ConnectionString;

                var request = HttpContext.Current?.Request;
                string userAgent = request?.UserAgent;
                if (userAgent != null && userAgent.Length > 300)
                    userAgent = userAgent.Substring(0, 300);

                using (var conn = new SqlConnection(connStr))
                {
                    conn.Open();
                    conn.Execute("meta.usp_Audit_Write", new
                    {
                        UserId = userId,
                        UserName = userName,
                        ActionType = actionType,
                        EntityName = entityName,
                        EntityKey = entityKey,
                        Detail = detail,
                        IpAddress = UserContext.GetClientIp(),
                        UserAgent = userAgent,
                        IsSuccess = isSuccess
                    }, commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                // audit ล้มเหลวต้องไม่ทำให้ผู้ใช้ทำงานต่อไม่ได้
                System.Diagnostics.Debug.WriteLine("[AuditLogger] " + ex.Message);
            }
        }
    }
}
