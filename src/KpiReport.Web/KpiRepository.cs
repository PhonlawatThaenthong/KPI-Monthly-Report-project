using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Dapper;
using KpiReport.Web.Models;

namespace KpiReport.Web.Repositories
{
    /// <summary>
    /// อ่านข้อมูล KPI จากชั้น rpt เท่านั้น (ไม่แตะ core/stg โดยตรง)
    /// เว็บมีสิทธิ์ SELECT/EXECUTE เฉพาะ schema rpt/meta ตามที่ตั้งไว้ตอนสร้าง role db_kpi_web
    /// </summary>
    public class KpiRepository
    {
        private readonly string _connectionString;

        public KpiRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        private SqlConnection Open()
        {
            var conn = new SqlConnection(_connectionString);
            conn.Open();
            return conn;
        }

        public int? GetLatestMonthKey()
        {
            // อ่านจาก rpt เท่านั้น ตามสิทธิ์ของ role db_kpi_web (ดู 01_database_and_schemas.sql)
            // ห้าม query core.FactKpiMonthly ตรง ๆ จากเว็บ แม้จะรู้สึกว่าเร็วกว่าก็ตาม
            using (var conn = Open())
            {
                return conn.ExecuteScalar<int?>(
                    "SELECT MAX(MonthKey) FROM rpt.vw_KpiMonthly");
            }
        }

        public List<KpiDashboardRow> GetDashboard(int monthKey, int departmentId)
        {
            using (var conn = Open())
            {
                var rows = conn.Query<KpiDashboardRow>(
                    "rpt.usp_GetKpiDashboard",
                    new { MonthKey = monthKey, DepartmentId = departmentId },
                    commandType: CommandType.StoredProcedure);
                return new List<KpiDashboardRow>(rows);
            }
        }

        public List<TrendPointViewModel> GetTrend(string kpiCode, int departmentId, int monthsBack)
        {
            using (var conn = Open())
            {
                var rows = conn.Query<TrendPointViewModel>(
                    "rpt.usp_GetKpiTrend",
                    new { KpiCode = kpiCode, DepartmentId = departmentId, MonthsBack = monthsBack },
                    commandType: CommandType.StoredProcedure);
                return new List<TrendPointViewModel>(rows);
            }
        }

        /// <summary>
        /// รายชื่อแผนกสำหรับ dropdown (Admin/Manager เท่านั้นที่เห็นตัวเลือกนี้)
        /// อ่านผ่าน rpt.vw_Department ตามสิทธิ์ของ role db_kpi_web (ไม่แตะ core ตรง ๆ)
        /// รวม "ทุกแผนก" (-99) ไว้บนสุด
        /// </summary>
        public List<DepartmentOption> GetDepartmentOptions()
        {
            const string sql = @"
                SELECT DepartmentId, DepartmentName
                FROM rpt.vw_Department
                ORDER BY CASE WHEN DepartmentId = -99 THEN 0 ELSE 1 END, DepartmentName";

            using (var conn = Open())
            {
                var rows = conn.Query<DepartmentOption>(sql);
                return new List<DepartmentOption>(rows);
            }
        }
    }
}
