using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Dapper;
using KpiReport.Etl.Models;

namespace KpiReport.Etl.Db
{
    /// <summary>
    /// จุดเดียวที่คุยกับฐานข้อมูลทั้งหมด
    /// เปิด/ปิด connection ต่อการเรียกแต่ละครั้ง (short-lived connection)
    /// เพราะ ETL รันเป็นรอบ ๆ ไม่ใช่ระบบที่ต้องการ connection pool ถาวรแบบเว็บ
    /// </summary>
    public class SqlDb
    {
        private readonly string _connectionString;

        public SqlDb(string connectionString)
        {
            _connectionString = connectionString;
        }

        private SqlConnection Open()
        {
            var conn = new SqlConnection(_connectionString);
            conn.Open();
            return conn;
        }

        // =========================================================
        // ETL RUN LOG
        // =========================================================

        public long EtlRunStart(string jobName, int? monthKey, string triggeredBy)
        {
            using (var conn = Open())
            {
                var p = new DynamicParameters();
                p.Add("@JobName", jobName);
                p.Add("@MonthKey", monthKey);
                p.Add("@TriggeredBy", triggeredBy);
                p.Add("@MachineName", Environment.MachineName);
                p.Add("@RunId", dbType: DbType.Int64, direction: ParameterDirection.Output);

                conn.Execute("meta.usp_EtlRun_Start", p, commandType: CommandType.StoredProcedure);
                return p.Get<long>("@RunId");
            }
        }

        public void EtlRunFinish(long runId, string status, int? rowsRead,
                                  int? rowsWritten, int? rowsRejected, string errorMessage)
        {
            using (var conn = Open())
            {
                conn.Execute("meta.usp_EtlRun_Finish", new
                {
                    RunId = runId,
                    Status = status,
                    RowsRead = rowsRead,
                    RowsWritten = rowsWritten,
                    RowsRejected = rowsRejected,
                    ErrorMessage = errorMessage
                }, commandType: CommandType.StoredProcedure);
            }
        }

        public void EtlStepLog(long runId, int stepNo, string stepName, string sourceName,
                                string status, int? rowsRead, int? rowsWritten,
                                int? rowsRejected, string message = null)
        {
            using (var conn = Open())
            {
                conn.Execute("meta.usp_EtlStep_Log", new
                {
                    RunId = runId,
                    StepNo = stepNo,
                    StepName = stepName,
                    SourceName = sourceName,
                    Status = status,
                    RowsRead = rowsRead,
                    RowsWritten = rowsWritten,
                    RowsRejected = rowsRejected,
                    Message = message
                }, commandType: CommandType.StoredProcedure);
            }
        }

        // =========================================================
        // FILE DEDUPE (stg.FileLoadHistory)
        // =========================================================

        public bool FileAlreadyLoaded(string fileHash)
        {
            using (var conn = Open())
            {
                int count = conn.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM stg.FileLoadHistory WHERE FileHash = @Hash",
                    new { Hash = fileHash });
                return count > 0;
            }
        }

        public void RecordFileLoad(long runId, string fileName, string fileHash,
                                    long fileSizeBytes, DateTime fileModifiedAt, int rowCount)
        {
            using (var conn = Open())
            {
                conn.Execute(@"
                    INSERT INTO stg.FileLoadHistory
                        (RunId, FileName, FileHash, FileSizeBytes, FileModifiedAt, RowCountLoaded)
                    VALUES
                        (@RunId, @FileName, @FileHash, @FileSizeBytes, @FileModifiedAt, @RowCount);",
                    new
                    {
                        RunId = runId,
                        FileName = fileName,
                        FileHash = fileHash,
                        FileSizeBytes = fileSizeBytes,
                        FileModifiedAt = fileModifiedAt,
                        RowCount = rowCount
                    });
            }
        }

        // =========================================================
        // BULK INSERT -> STAGING
        // =========================================================

        public void BulkInsertDowntimeRaw(long runId, List<DowntimeRawRow> rows)
        {
            if (rows.Count == 0) return;

            var table = new DataTable();
            table.Columns.Add("RunId", typeof(long));
            table.Columns.Add("SourceFileName", typeof(string));
            table.Columns.Add("SourceLineNo", typeof(int));
            table.Columns.Add("EventDate", typeof(string));
            table.Columns.Add("DepartmentText", typeof(string));
            table.Columns.Add("MachineCode", typeof(string));
            table.Columns.Add("ReasonCode", typeof(string));
            table.Columns.Add("ReasonText", typeof(string));
            table.Columns.Add("StartTimeText", typeof(string));
            table.Columns.Add("EndTimeText", typeof(string));
            table.Columns.Add("DurationMinText", typeof(string));

            foreach (var r in rows)
            {
                table.Rows.Add(
                    runId,
                    (object)r.SourceFileName ?? DBNull.Value,
                    (object)r.SourceLineNo ?? DBNull.Value,
                    (object)r.EventDate ?? DBNull.Value,
                    (object)r.DepartmentText ?? DBNull.Value,
                    (object)r.MachineCode ?? DBNull.Value,
                    (object)r.ReasonCode ?? DBNull.Value,
                    (object)r.ReasonText ?? DBNull.Value,
                    (object)r.StartTimeText ?? DBNull.Value,
                    (object)r.EndTimeText ?? DBNull.Value,
                    (object)r.DurationMinText ?? DBNull.Value);
            }

            BulkCopy(table, "stg.DowntimeRaw");
        }

        public void BulkInsertAttendanceRaw(long runId, List<AttendanceRawRow> rows)
        {
            if (rows.Count == 0) return;

            var table = new DataTable();
            table.Columns.Add("RunId", typeof(long));
            table.Columns.Add("SourceFileName", typeof(string));
            table.Columns.Add("SourceLineNo", typeof(int));
            table.Columns.Add("WorkDate", typeof(string));
            table.Columns.Add("EmployeeCode", typeof(string));
            table.Columns.Add("EmployeeName", typeof(string));
            table.Columns.Add("DepartmentText", typeof(string));
            table.Columns.Add("StatusText", typeof(string));
            table.Columns.Add("WorkHoursText", typeof(string));
            table.Columns.Add("OtHoursText", typeof(string));

            foreach (var r in rows)
            {
                table.Rows.Add(
                    runId,
                    (object)r.SourceFileName ?? DBNull.Value,
                    (object)r.SourceLineNo ?? DBNull.Value,
                    (object)r.WorkDate ?? DBNull.Value,
                    (object)r.EmployeeCode ?? DBNull.Value,
                    (object)r.EmployeeName ?? DBNull.Value,
                    (object)r.DepartmentText ?? DBNull.Value,
                    (object)r.StatusText ?? DBNull.Value,
                    (object)r.WorkHoursText ?? DBNull.Value,
                    (object)r.OtHoursText ?? DBNull.Value);
            }

            BulkCopy(table, "stg.AttendanceRaw");
        }

        public (int written, int rejected) TransformAttendance(long runId)
        {
            using (var conn = Open())
            {
                var p = new DynamicParameters();
                p.Add("@RunId", runId);
                p.Add("@RowsWritten", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("@RowsRejected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                conn.Execute("core.usp_Transform_Attendance", p,
                    commandType: CommandType.StoredProcedure, commandTimeout: 120);

                return (p.Get<int>("@RowsWritten"), p.Get<int>("@RowsRejected"));
            }
        }

        public void BulkInsertCostRaw(long runId, List<CostRawRow> rows)
        {
            if (rows.Count == 0) return;

            var table = new DataTable();
            table.Columns.Add("RunId", typeof(long));
            table.Columns.Add("SourceFileName", typeof(string));
            table.Columns.Add("SourceSheetName", typeof(string));
            table.Columns.Add("SourceRowNo", typeof(int));
            table.Columns.Add("PeriodText", typeof(string));
            table.Columns.Add("DepartmentText", typeof(string));
            table.Columns.Add("CostTypeText", typeof(string));
            table.Columns.Add("AmountText", typeof(string));
            table.Columns.Add("CurrencyText", typeof(string));
            table.Columns.Add("Remark", typeof(string));

            foreach (var r in rows)
            {
                table.Rows.Add(
                    runId,
                    (object)r.SourceFileName ?? DBNull.Value,
                    (object)r.SourceSheetName ?? DBNull.Value,
                    (object)r.SourceRowNo ?? DBNull.Value,
                    (object)r.PeriodText ?? DBNull.Value,
                    (object)r.DepartmentText ?? DBNull.Value,
                    (object)r.CostTypeText ?? DBNull.Value,
                    (object)r.AmountText ?? DBNull.Value,
                    (object)r.CurrencyText ?? DBNull.Value,
                    (object)r.Remark ?? DBNull.Value);
            }

            BulkCopy(table, "stg.CostRaw");
        }

        private void BulkCopy(DataTable table, string destinationTable)
        {
            using (var conn = Open())
            using (var bulk = new SqlBulkCopy(conn) { DestinationTableName = destinationTable })
            {
                foreach (DataColumn col in table.Columns)
                    bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);

                bulk.WriteToServer(table);
            }
        }

        // =========================================================
        // TRANSFORM (staging -> core)
        // =========================================================

        public (int written, int rejected) TransformDowntime(long runId)
        {
            using (var conn = Open())
            {
                var p = new DynamicParameters();
                p.Add("@RunId", runId);
                p.Add("@RowsWritten", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("@RowsRejected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                conn.Execute("core.usp_Transform_Downtime", p,
                    commandType: CommandType.StoredProcedure, commandTimeout: 120);

                return (p.Get<int>("@RowsWritten"), p.Get<int>("@RowsRejected"));
            }
        }

        public (int written, int rejected) TransformCost(long runId)
        {
            using (var conn = Open())
            {
                var p = new DynamicParameters();
                p.Add("@RunId", runId);
                p.Add("@RowsWritten", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("@RowsRejected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                conn.Execute("core.usp_Transform_Cost", p,
                    commandType: CommandType.StoredProcedure, commandTimeout: 120);

                return (p.Get<int>("@RowsWritten"), p.Get<int>("@RowsRejected"));
            }
        }

        // =========================================================
        // PRODUCTION (extract + transform ทำในฝั่ง SQL ล้วน)
        // =========================================================

        public void RunEtlProduction(string triggeredBy)
        {
            using (var conn = Open())
            {
                conn.Execute("core.usp_RunEtl_Production",
                    new { TriggeredBy = triggeredBy },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 300);
            }
        }

        // =========================================================
        // KPI CALCULATION
        // =========================================================

        public void RunKpiMonthly(int monthKey, string triggeredBy)
        {
            using (var conn = Open())
            {
                conn.Execute("core.usp_RunKpi_Monthly",
                    new { MonthKey = monthKey, TriggeredBy = triggeredBy },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 300);
            }
        }

        public void RunKpiAllMonths(string triggeredBy)
        {
            using (var conn = Open())
            {
                conn.Execute("core.usp_RunKpi_AllMonths",
                    new { TriggeredBy = triggeredBy },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 600);
            }
        }
    }
}
