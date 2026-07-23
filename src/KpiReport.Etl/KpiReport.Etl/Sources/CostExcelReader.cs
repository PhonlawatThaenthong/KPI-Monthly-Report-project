using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using KpiReport.Etl.Models;

namespace KpiReport.Etl.Sources
{
    /// <summary>
    /// อ่าน Cost_Master.xlsx ทุก sheet (1 sheet = 1 เดือน)
    ///
    /// รูปแบบไฟล์จริงจากฝ่ายบัญชี:
    ///   แถว 1-2 = title / ข้อความ confidential (junk header ต้องข้าม)
    ///   แถว 3   = หัวตารางจริง (Period, Department, CostType, Amount, Currency, Remark)
    ///   แถว 4+  = ข้อมูล จนกว่าจะเจอแถวว่าง
    /// </summary>
    public static class CostExcelReader
    {
        private const int HeaderRow = 3;

        public static List<CostRawRow> Read(string filePath)
        {
            var rows = new List<CostRawRow>();

            using (var workbook = new XLWorkbook(filePath))
            {
                foreach (var ws in workbook.Worksheets)
                {
                    int row = HeaderRow + 1;

                    while (true)
                    {
                        string period = ws.Cell(row, 1).GetString().Trim();
                        string dept   = ws.Cell(row, 2).GetString().Trim();

                        // แถวว่างทั้งคอลัมน์ Period และ Department = จบข้อมูลของ sheet นี้
                        if (string.IsNullOrEmpty(period) && string.IsNullOrEmpty(dept))
                            break;

                        rows.Add(new CostRawRow
                        {
                            SourceFileName  = Path.GetFileName(filePath),
                            SourceSheetName = ws.Name,
                            SourceRowNo     = row,
                            PeriodText      = period,
                            DepartmentText  = dept,
                            CostTypeText    = ws.Cell(row, 3).GetString().Trim(),
                            AmountText      = ws.Cell(row, 4).GetString().Trim(),
                            CurrencyText    = ws.Cell(row, 5).GetString().Trim(),
                            Remark          = ws.Cell(row, 6).GetString().Trim(),
                        });

                        row++;

                        // กันลูปไม่รู้จบถ้าไฟล์เพี้ยน (sheet ปกติไม่เกิน ~40 แถวข้อมูล)
                        if (row > HeaderRow + 5000)
                            break;
                    }
                }
            }

            return rows;
        }
    }
}
