using System;
using System.Configuration;
using System.Data.SqlClient;
using Dapper;

namespace KpiReport.Web.Infrastructure
{
    /// <summary>
    /// จำกัดจำนวนคำขอตั้งรหัสผ่านใหม่ต่ออีเมลและต่อ IP
    ///
    /// ทำไมต้องมี
    /// ---------------------------------------------------------------
    /// หน้า "ลืมรหัสผ่าน" เปิดให้คนที่ยังไม่ล็อกอินใช้ได้ ถ้าไม่จำกัดจำนวน
    /// ใครก็กรอกอีเมลคนอื่นแล้วกดรัว ๆ จนกล่องจดหมายเขาเต็มได้
    /// และเป็นการยิงภาระใส่เซิร์ฟเวอร์เมลของบริษัทไปในตัว
    ///
    /// ใช้ meta.AuditLog ที่มีอยู่แล้วเป็นตัวนับ ไม่สร้างตารางใหม่
    /// เพราะทุกคำขอถูกบันทึกลง audit อยู่แล้วด้วยเหตุผลด้านความปลอดภัย
    /// ตัวนับกับหลักฐานจึงเป็นข้อมูลชุดเดียวกัน ไม่มีทางไม่ตรงกัน
    /// </summary>
    public static class PasswordResetThrottle
    {
        public const string RequestedAction = "PASSWORD_RESET_REQUESTED";

        private const int WindowMinutes = 15;
        private const int MaxPerEmail = 3;
        private const int MaxPerIp = 10;   // เผื่อทั้งออฟฟิศออกเน็ตผ่าน IP เดียวกัน

        /// <summary>
        /// true = ขอบ่อยเกินไป ให้ปฏิเสธ
        ///
        /// นับแยกสองแกน: ต่ออีเมล กันการถล่มกล่องจดหมายคนใดคนหนึ่ง
        /// และต่อ IP กันการไล่ยิงหลายอีเมลจากที่เดียว
        /// เกณฑ์ IP ตั้งหลวมกว่าเพราะสำนักงานมัก NAT ออกไอพีเดียวกันทั้งตึก
        /// </summary>
        public static bool IsRateLimited(string email, string ipAddress)
        {
            try
            {
                string connStr = ConfigurationManager.ConnectionStrings["KpiDb"].ConnectionString;

                using (var conn = new SqlConnection(connStr))
                {
                    conn.Open();

                    int byEmail = conn.ExecuteScalar<int>(@"
                        SELECT COUNT(1) FROM meta.AuditLog
                        WHERE ActionType = @Action
                          AND UserName   = @Email
                          AND OccurredAt >= DATEADD(MINUTE, -@Minutes, SYSDATETIME())",
                        new { Action = RequestedAction, Email = email, Minutes = WindowMinutes });

                    if (byEmail >= MaxPerEmail) return true;

                    if (string.IsNullOrEmpty(ipAddress)) return false;

                    int byIp = conn.ExecuteScalar<int>(@"
                        SELECT COUNT(1) FROM meta.AuditLog
                        WHERE ActionType = @Action
                          AND IpAddress  = @Ip
                          AND OccurredAt >= DATEADD(MINUTE, -@Minutes, SYSDATETIME())",
                        new { Action = RequestedAction, Ip = ipAddress, Minutes = WindowMinutes });

                    return byIp >= MaxPerIp;
                }
            }
            catch (Exception ex)
            {
                // ตัวนับล่มต้องไม่ทำให้คนที่ลืมรหัสจริง ๆ ใช้งานไม่ได้
                // ยอมปล่อยผ่าน แล้วให้เห็นใน Output window ตอนพัฒนา
                System.Diagnostics.Debug.WriteLine("[PasswordResetThrottle] " + ex.Message);
                return false;
            }
        }

        public static string DescribeLimit()
        {
            return "ขอได้ไม่เกิน " + MaxPerEmail + " ครั้งต่อ " + WindowMinutes + " นาที";
        }
    }
}
