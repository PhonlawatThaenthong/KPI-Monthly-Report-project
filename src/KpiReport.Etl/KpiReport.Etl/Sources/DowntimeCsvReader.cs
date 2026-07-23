using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualBasic.FileIO;   // ต้อง Add Reference: Microsoft.VisualBasic
using KpiReport.Etl.Infrastructure;
using KpiReport.Etl.Models;

namespace KpiReport.Etl.Sources
{
    /// <summary>
    /// อ่านไฟล์ CSV เครื่องหยุด 1 ไฟล์ -> list ของแถวดิบ
    /// ใช้ TextFieldParser แทนการ split(',') เอง เพราะรองรับ
    /// ฟิลด์ที่มี comma อยู่ในเครื่องหมายคำพูดได้ถูกต้อง
    /// จับคู่คอลัมน์ด้วยชื่อ header ไม่ใช่ตำแหน่ง
    /// เผื่อไฟล์ในอนาคตสลับลำดับคอลัมน์
    /// </summary>
    public static class DowntimeCsvReader
    {
        public static List<DowntimeRawRow> Read(string filePath)
        {
            var rows = new List<DowntimeRawRow>();
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
                        // แถวที่ parse ไม่ได้เลย (rare) -> ข้าม แต่แจ้งเตือนใน console
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

                    rows.Add(new DowntimeRawRow
                    {
                        SourceFileName  = Path.GetFileName(filePath),
                        SourceLineNo    = lineNo,
                        EventDate       = GetOrNull(map, "EventDate"),
                        DepartmentText  = GetOrNull(map, "Department"),
                        MachineCode     = GetOrNull(map, "MachineCode"),
                        ReasonCode      = GetOrNull(map, "ReasonCode"),
                        ReasonText      = GetOrNull(map, "ReasonText"),
                        StartTimeText   = GetOrNull(map, "StartTime"),
                        EndTimeText     = GetOrNull(map, "EndTime"),
                        DurationMinText = GetOrNull(map, "DurationMin"),
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
