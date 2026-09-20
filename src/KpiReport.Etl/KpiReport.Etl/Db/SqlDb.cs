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

        public void BulkInsertKpiEmployeeFeedRaw(long runId, List<KpiFeedRow> rows)
        {
            if (rows.Count == 0) return;

            var table = new DataTable();
            table.Columns.Add("RunId", typeof(long));
            table.Columns.Add("SourceName", typeof(string));
            table.Columns.Add("SourceLineNo", typeof(int));
            table.Columns.Add("MonthText", typeof(string));
            table.Columns.Add("EmployeeCodeText", typeof(string));
            table.Columns.Add("EmployeeNameText", typeof(string));
            table.Columns.Add("DepartmentText", typeof(string));
            table.Columns.Add("KpiCodeText", typeof(string));
            table.Columns.Add("TargetValueText", typeof(string));
            table.Columns.Add("ActualValueText", typeof(string));
            table.Columns.Add("StatusText", typeof(string));
            table.Columns.Add("CompletedDateText", typeof(string));

            foreach (var r in rows)
            {
                table.Rows.Add(
                    runId,
                    (object)r.SourceName ?? DBNull.Value,
                    (object)r.SourceLineNo ?? DBNull.Value,
                    (object)r.MonthText ?? DBNull.Value,
                    (object)r.EmployeeCodeText ?? DBNull.Value,
                    (object)r.EmployeeNameText ?? DBNull.Value,
                    (object)r.DepartmentText ?? DBNull.Value,
                    (object)r.KpiCodeText ?? DBNull.Value,
                    (object)r.TargetValueText ?? DBNull.Value,
                    (object)r.ActualValueText ?? DBNull.Value,
                    (object)r.StatusText ?? DBNull.Value,
                    (object)r.CompletedDateText ?? DBNull.Value);
            }

            BulkCopy(table, "stg.KpiEmployeeFeedRaw");
        }

        /// <summary>
        /// stg.KpiEmployeeFeedRaw -> core.FactKpiEmployeeMonthly
        /// แถวที่รับไม่ได้จะถูกบันทึกเหตุผลไว้ที่ meta.DataRejectLog ไม่หายเงียบ ๆ
        /// </summary>
        public (int written, int rejected) TransformKpiEmployeeFeed(long runId)
        {
            using (var conn = Open())
            {
                var p = new DynamicParameters();
                p.Add("@RunId", runId);
                p.Add("@RowsWritten", dbType: DbType.Int32, direction: ParameterDirection.Output);
                p.Add("@RowsRejected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                conn.Execute("core.usp_Transform_KpiEmployeeFeed", p,
                    commandType: CommandType.StoredProcedure, commandTimeout: 120);

                return (p.Get<int>("@RowsWritten"), p.Get<int>("@RowsRejected"));
            }
        }

        /// <summary>
        /// core.FactKpiEmployeeMonthly -> core.FactKpiMonthly (ระดับแผนก)
        ///
        /// ตารางระดับแผนกเป็น "ผลรวมที่สร้างใหม่ได้เสมอ" ไม่ใช่แหล่งข้อมูลอิสระ
        /// จึงต้องเรียกทุกครั้งหลังโหลดข้อมูลรายบุคคลเข้าไป ไม่งั้น Dashboard
        /// กับหน้า Monitoring จะพูดคนละเรื่องกัน
        ///
        /// proc นี้เรียก core.usp_RefreshKpi_Derived (ค่าเดือนก่อน + สีสถานะ) ให้เองแล้ว
        /// </summary>
        public void RollupKpiToDepartment(int? monthKey = null)
        {
            using (var conn = Open())
            {
                conn.Execute("core.usp_Rollup_KpiEmployeeToDept",
                    new { MonthKey = monthKey },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 300);
            }
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
        // ค่าที่ต้องคำนวณต่อจากค่าที่ต้นทางส่งมา
        //
        // ระบบนี้ไม่คำนวณ KPI เองแล้ว เหลือแค่ PrevMonthValue (ค่าเดือนก่อน)
        // และ StatusFlag (สีเขียว/เหลือง/แดง) ซึ่งต้องมองข้ามเดือนและอิง
        // ทิศทาง H/L ของตัวชี้วัด — ต้นทางที่ส่งมาทีละเดือนทำแทนให้ไม่ได้
        //
        // ปกติ usp_Rollup_KpiEmployeeToDept เรียกให้เองอยู่แล้วตอนจบการโหลด
        // เมธอดนี้ไว้สั่งซ้ำตอนแก้เป้าหมายย้อนหลังหรือแก้ทิศทางของ KPI
        // =========================================================

        public void RefreshKpiDerived(int? fromMonthKey = null)
        {
            using (var conn = Open())
            {
                conn.Execute("core.usp_RefreshKpi_Derived",
                    new { FromMonthKey = fromMonthKey },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 300);
            }
        }
    }
}
