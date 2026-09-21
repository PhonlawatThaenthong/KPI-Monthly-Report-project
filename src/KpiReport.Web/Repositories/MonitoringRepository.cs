using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Dapper;
using KpiReport.Web.Models;

namespace KpiReport.Web.Repositories
{
    /// <summary>
    /// อ่านความคืบหน้าการทำ KPI รายบุคคล/รายแผนก จากชั้น rpt เท่านั้น
    ///
    /// ทุกเมธอดรับ departmentIdCsv ('1,3,5') ซึ่งผู้เรียกต้องกรองสิทธิ์มาก่อนแล้ว
    /// (ดู BaseController.ResolveDepartmentScope) ตัวนี้ไม่ตัดสินใจเรื่องสิทธิ์เอง
    /// แต่ส่งต่อเป็น parameter ไม่ต่อสตริงเป็น SQL จึงไม่มีช่อง injection
    /// </summary>
    public class MonitoringRepository
    {
        private readonly string _connectionString;

        public MonitoringRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        private SqlConnection Open()
        {
            var conn = new SqlConnection(_connectionString);
            conn.Open();
            return conn;
        }

        public List<DepartmentCompletionRow> GetDepartmentCompletion(int monthKey, string departmentIdCsv)
        {
            using (var conn = Open())
            {
                var rows = conn.Query<DepartmentCompletionRow>(
                    "rpt.usp_GetDepartmentMonitoring",
                    new { MonthKey = monthKey, DepartmentIds = departmentIdCsv },
                    commandType: CommandType.StoredProcedure);
                return new List<DepartmentCompletionRow>(rows);
            }
        }

        public List<EmployeeCompletionRow> GetEmployeeSummary(int monthKey, string departmentIdCsv)
        {
            using (var conn = Open())
            {
                var rows = conn.Query<EmployeeCompletionRow>(
                    "rpt.usp_GetEmployeeKpiSummary",
                    new { MonthKey = monthKey, DepartmentIds = departmentIdCsv },
                    commandType: CommandType.StoredProcedure);
                return new List<EmployeeCompletionRow>(rows);
            }
        }

        public List<EmployeeKpiRow> GetEmployeeKpi(int monthKey, string departmentIdCsv,
                                                   int? employeeId = null, bool onlyIncomplete = false)
        {
            using (var conn = Open())
            {
                var rows = conn.Query<EmployeeKpiRow>(
                    "rpt.usp_GetEmployeeKpiStatus",
                    new
                    {
                        MonthKey = monthKey,
                        DepartmentIds = departmentIdCsv,
                        EmployeeId = employeeId,
                        OnlyIncomplete = onlyIncomplete
                    },
                    commandType: CommandType.StoredProcedure);
                return new List<EmployeeKpiRow>(rows);
            }
        }

        /// <summary>
        /// เดือนที่มีข้อมูลจริง เรียงใหม่สุดก่อน — ใช้ทำ dropdown เลือกเดือน
        /// อิง rpt.vw_ValidMonth ตัวเดียวกับที่ Dashboard ใช้ เดือนผีจึงไม่โผล่
        /// </summary>
        public List<int> GetAvailableMonths(int take = 24)
        {
            using (var conn = Open())
            {
                var rows = conn.Query<int>(
                    "SELECT TOP (@Take) MonthKey FROM rpt.vw_ValidMonth ORDER BY MonthKey DESC",
                    new { Take = take });
                return new List<int>(rows);
            }
        }
    }
}
