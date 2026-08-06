using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using KpiReport.Etl.Infrastructure;
using KpiReport.Etl.Models;

namespace KpiReport.Etl.Sources
{
    /// <summary>
    /// อ่านไฟล์ CSV ลงเวลา 1 ไฟล์ -> list ของแถวดิบ
    /// โครงสร้างเหมือน DowntimeCsvReader (TextFieldParser + จับคู่ด้วยชื่อ header)
    /// </summary>
    public static class AttendanceCsvReader
    {
        public static List<AttendanceRawRow> Read(string filePath)
        {
            var rows = new List<AttendanceRawRow>();
            var encoding = CsvEncodingDetector.Detect(filePath);

            using (var parser = new TextFieldParser(filePath, encoding))
            {
                parser.TextFieldType = FieldType.Delimited;
                parser.SetDelimiters(",");
                parser.HasFieldsEnclosedInQuotes = true;
                parser.TrimWhiteSpace = true;

                string[] header = null;
                int lineNo = 1;

                while (!parser.EndOfData)
                {
                    string[] fields;
                    try
                    {
                        fields = parser.ReadFields();
                    }
                    catch (MalformedLineException ex)
                    {
                        Console.Error.WriteLine(
                            $"[warn] ข้าม malformed line ที่ {filePath} บรรทัด {lineNo}: {ex.Message}");
                        lineNo++;
                        continue;
                    }

                    if (header == null)
                    {
                        header = fields;
                        lineNo++;
                        continue;
                    }

                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < header.Length && i < fields.Length; i++)
                        map[header[i].Trim()] = fields[i];

                    rows.Add(new AttendanceRawRow
                    {
                        SourceFileName = Path.GetFileName(filePath),
                        SourceLineNo   = lineNo,
                        WorkDate       = GetOrNull(map, "WorkDate"),
                        EmployeeCode   = GetOrNull(map, "EmployeeCode"),
                        EmployeeName   = GetOrNull(map, "EmployeeName"),
                        DepartmentText = GetOrNull(map, "Department"),
                        StatusText     = GetOrNull(map, "Status"),
                        WorkHoursText  = GetOrNull(map, "WorkHours"),
                        OtHoursText    = GetOrNull(map, "OtHours"),
                    });

                    lineNo++;
                }
            }

            return rows;
        }

        private static string GetOrNull(Dictionary<string, string> map, string key)
        {
            return map.TryGetValue(key, out var v) ? v : null;
        }
    }
}
