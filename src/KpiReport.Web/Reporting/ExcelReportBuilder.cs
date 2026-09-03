using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using KpiReport.Web.Models;

namespace KpiReport.Web.Reporting
{
    /// <summary>
    /// สร้างไฟล์ Excel รายงาน KPI รายเดือน (ClosedXML)
    ///
    /// Sheet 1 "Summary"       — KPI ตามขอบเขตที่ผู้ใช้เห็น
    /// Sheet 2 "By Department" — สร้างเฉพาะเมื่อผู้ใช้เห็นได้ทุกแผนก
    ///
    /// คืนค่าเป็น byte[] ไม่เขียนลงดิสก์ เพราะเว็บส่งกลับเป็น FileResult ตรง ๆ
    /// และเซิร์ฟเวอร์ไม่ต้องมีสิทธิ์เขียนไฟล์
    /// </summary>
    public static class ExcelReportBuilder
    {
        // สีชุดเดียวกับธีมเว็บ (kpi-theme.css) เพื่อให้ไฟล์ที่ export ดูเป็นชุดเดียวกับหน้าจอ
        private static readonly XLColor HeaderBg = XLColor.FromHtml("#1B4D89");
        private static readonly XLColor TitleColor = XLColor.FromHtml("#0B2545");
        private static readonly XLColor MutedColor = XLColor.FromHtml("#5C6B7A");
        private static readonly XLColor BorderColor = XLColor.FromHtml("#DDE3EA");
        private static readonly XLColor ZebraBg = XLColor.FromHtml("#FAFBFD");

        private static readonly XLColor GreenBg = XLColor.FromHtml("#E7F5EC");
        private static readonly XLColor GreenFg = XLColor.FromHtml("#1A7F37");
        private static readonly XLColor AmberBg = XLColor.FromHtml("#FDF3D8");
        private static readonly XLColor AmberFg = XLColor.FromHtml("#9A6700");
        private static readonly XLColor RedBg = XLColor.FromHtml("#FDECEB");
        private static readonly XLColor RedFg = XLColor.FromHtml("#B42318");

        public static byte[] Build(KpiReportData data)
        {
            if (data == null) throw new ArgumentNullException("data");

            using (var wb = new XLWorkbook())
            {
                BuildSummarySheet(wb, data);

                if (data.HasDepartmentBreakdown)
                    BuildDepartmentSheet(wb, data);

                using (var ms = new MemoryStream())
                {
                    wb.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        // ---------------------------------------------------------------
        // Sheet 1 : Summary
        // ---------------------------------------------------------------
        private static void BuildSummarySheet(XLWorkbook wb, KpiReportData data)
        {
            var ws = wb.Worksheets.Add("Summary");

            // --- หัวรายงาน ---
            ws.Cell(1, 1).Value = "HR KPI Monthly Report";
            ws.Range(1, 1, 1, 9).Merge();
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(TitleColor);

            ws.Cell(2, 1).Value = "Period: " + data.MonthLabel + "    |    Scope: " + data.ScopeLabel;
            ws.Range(2, 1, 2, 9).Merge();
            ws.Cell(2, 1).Style.Font.SetFontColor(MutedColor).Font.SetFontSize(10);

            ws.Cell(3, 1).Value = "Generated: " + data.GeneratedAtText
                                  + "    |    By: " + (data.GeneratedBy ?? "-");
            ws.Range(3, 1, 3, 9).Merge();
            ws.Cell(3, 1).Style.Font.SetFontColor(MutedColor).Font.SetFontSize(10);

            // --- แถบสรุปจำนวนตามสถานะ ---
            ws.Cell(5, 1).Value = "Total KPIs";
            ws.Cell(5, 2).Value = data.Rows.Count;
            ws.Cell(5, 4).Value = "On Target";
            ws.Cell(5, 5).Value = data.CountGreen;
            ws.Cell(5, 6).Value = "Watch";
            ws.Cell(5, 7).Value = data.CountYellow;
            ws.Cell(5, 8).Value = "Below Target";
            ws.Cell(5, 9).Value = data.CountRed;

            foreach (int c in new[] { 1, 4, 6, 8 })
                ws.Cell(5, c).Style.Font.SetBold().Font.SetFontSize(10).Font.SetFontColor(MutedColor);

            ws.Cell(5, 5).Style.Font.SetBold().Font.SetFontColor(GreenFg);
            ws.Cell(5, 7).Style.Font.SetBold().Font.SetFontColor(AmberFg);
            ws.Cell(5, 9).Style.Font.SetBold().Font.SetFontColor(RedFg);
            ws.Cell(5, 2).Style.Font.SetBold();

            // --- ตาราง ---
            const int headerRow = 7;
            string[] headers =
            {
                "#", "KPI Code", "KPI Name", "Unit", "Actual",
                "Target", "Achievement %", "MoM %", "Status"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(headerRow, i + 1);
                cell.Value = headers[i];
                StyleHeaderCell(cell);
            }

            int row = headerRow + 1;
            int index = 1;

            foreach (var k in data.Rows)
            {
                ws.Cell(row, 1).Value = index;
                ws.Cell(row, 2).Value = k.KpiCode ?? "";
                ws.Cell(row, 3).Value = k.KpiName ?? "";
                ws.Cell(row, 4).Value = k.Unit ?? "";

                SetNumber(ws.Cell(row, 5), k.ActualValue, k.DecimalPlaces);
                SetNumber(ws.Cell(row, 6), k.TargetValue, k.DecimalPlaces);
                SetNumber(ws.Cell(row, 7), k.AchievementPct, 1);
                SetNumber(ws.Cell(row, 8), k.MoMChangePct, 1);

                StyleStatusCell(ws.Cell(row, 9), k.StatusFlag);

                ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                if (index % 2 == 0)
                    ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = ZebraBg;

                row++;
                index++;
            }

            int lastRow = row - 1;
            if (lastRow >= headerRow + 1)
            {
                var body = ws.Range(headerRow, 1, lastRow, headers.Length);
                body.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                body.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                body.Style.Border.OutsideBorderColor = BorderColor;
                body.Style.Border.InsideBorderColor = BorderColor;

                ws.Range(headerRow, 1, lastRow, headers.Length).SetAutoFilter();
            }

            // ตรึงหัวตารางไว้ ให้เลื่อนดูข้อมูลยาว ๆ แล้วยังเห็นชื่อคอลัมน์
            ws.SheetView.FreezeRows(headerRow);

            ws.Columns(1, headers.Length).AdjustToContents();
            ws.Column(1).Width = 5;
            if (ws.Column(3).Width < 30) ws.Column(3).Width = 30;

            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.FitToPages(1, 0);
        }

        // ---------------------------------------------------------------
        // Sheet 2 : By Department
        // ---------------------------------------------------------------
        private static void BuildDepartmentSheet(XLWorkbook wb, KpiReportData data)
        {
            var ws = wb.Worksheets.Add("By Department");

            ws.Cell(1, 1).Value = "KPI by Department";
            ws.Range(1, 1, 1, 8).Merge();
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(TitleColor);

            ws.Cell(2, 1).Value = "Period: " + data.MonthLabel;
            ws.Range(2, 1, 2, 8).Merge();
            ws.Cell(2, 1).Style.Font.SetFontColor(MutedColor).Font.SetFontSize(10);

            const int headerRow = 4;
            string[] headers =
            {
                "Department", "KPI Code", "KPI Name", "Unit",
                "Actual", "Target", "Achievement %", "Status"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(headerRow, i + 1);
                cell.Value = headers[i];
                StyleHeaderCell(cell);
            }

            int row = headerRow + 1;
            int index = 1;
            string previousDept = null;

            foreach (var k in data.DepartmentRows)
            {
                ws.Cell(row, 1).Value = k.DepartmentName ?? "";
                ws.Cell(row, 2).Value = k.KpiCode ?? "";
                ws.Cell(row, 3).Value = k.KpiName ?? "";
                ws.Cell(row, 4).Value = k.Unit ?? "";

                SetNumber(ws.Cell(row, 5), k.ActualValue, k.DecimalPlaces);
                SetNumber(ws.Cell(row, 6), k.TargetValue, k.DecimalPlaces);
                SetNumber(ws.Cell(row, 7), k.AchievementPct, 1);

                StyleStatusCell(ws.Cell(row, 8), k.StatusFlag);

                ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // ขีดเส้นหนาคั่นเมื่อเปลี่ยนแผนก อ่านง่ายกว่าเส้นเท่ากันหมด
                if (previousDept != null && previousDept != k.DepartmentName)
                {
                    ws.Range(row, 1, row, headers.Length).Style.Border.TopBorder = XLBorderStyleValues.Medium;
                    ws.Range(row, 1, row, headers.Length).Style.Border.TopBorderColor = HeaderBg;
                }
                else if (index % 2 == 0)
                {
                    ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = ZebraBg;
                }

                previousDept = k.DepartmentName;
                row++;
                index++;
            }

            int lastRow = row - 1;
            if (lastRow >= headerRow + 1)
            {
                var body = ws.Range(headerRow, 1, lastRow, headers.Length);
                body.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                body.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                body.Style.Border.OutsideBorderColor = BorderColor;
                body.Style.Border.InsideBorderColor = BorderColor;

                ws.Range(headerRow, 1, lastRow, headers.Length).SetAutoFilter();
            }

            ws.SheetView.FreezeRows(headerRow);
            ws.Columns(1, headers.Length).AdjustToContents();
            if (ws.Column(3).Width < 30) ws.Column(3).Width = 30;

            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.FitToPages(1, 0);
        }

        // ---------------------------------------------------------------
        // helper
        // ---------------------------------------------------------------
        private static void StyleHeaderCell(IXLCell cell)
        {
            cell.Style.Fill.BackgroundColor = HeaderBg;
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Font.SetFontSize(10);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        /// <summary>
        /// เขียนตัวเลขเป็น "ตัวเลขจริง" ไม่ใช่ข้อความ ผู้ใช้จะได้เอาไป sum/pivot ต่อได้
        /// ค่าว่างใส่ "-" ไว้ให้อ่านออกว่าไม่มีข้อมูล ไม่ใช่ศูนย์
        /// </summary>
        private static void SetNumber(IXLCell cell, decimal? value, int decimals)
        {
            if (value.HasValue)
            {
                cell.Value = (double)value.Value;
                cell.Style.NumberFormat.Format = decimals > 0
                    ? "#,##0." + new string('0', decimals)
                    : "#,##0";
            }
            else
            {
                cell.Value = "-";
                cell.Style.Font.SetFontColor(MutedColor);
            }

            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        private static void StyleStatusCell(IXLCell cell, string statusFlag)
        {
            string flag = (statusFlag ?? "").ToUpperInvariant();

            switch (flag)
            {
                case "GREEN":
                    cell.Value = "On Target";
                    cell.Style.Fill.BackgroundColor = GreenBg;
                    cell.Style.Font.SetFontColor(GreenFg).Font.SetBold();
                    break;
                case "YELLOW":
                    cell.Value = "Watch";
                    cell.Style.Fill.BackgroundColor = AmberBg;
                    cell.Style.Font.SetFontColor(AmberFg).Font.SetBold();
                    break;
                case "RED":
                    cell.Value = "Below Target";
                    cell.Style.Fill.BackgroundColor = RedBg;
                    cell.Style.Font.SetFontColor(RedFg).Font.SetBold();
                    break;
                default:
                    cell.Value = "N/A";
                    cell.Style.Font.SetFontColor(MutedColor);
                    break;
            }

            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }
}
