using System;
using System.Collections.Generic;
using System.IO;
using KpiReport.Web.Models;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace KpiReport.Web.Reporting
{
    /// <summary>
    /// สร้างไฟล์ PDF รายงาน KPI รายเดือน (MigraDoc + PDFsharp 1.50)
    ///
    /// โครงหน้า: หัวรายงาน -> สรุปจำนวนตามสถานะ -> ตาราง KPI -> ตารางแยกรายแผนก -> เลขหน้า
    ///
    /// หมายเหตุภาษา: รายงานนี้เป็นภาษาอังกฤษล้วน จึงใช้ Arial ที่มีอยู่ในเครื่องได้เลย
    /// ถ้าวันหลังอยากได้ภาษาไทย ต้อง embed ฟอนต์ไทย (เช่น Sarabun) เพิ่ม
    /// </summary>
    public static class PdfReportBuilder
    {
        private static readonly Color Navy = new Color(11, 37, 69);
        private static readonly Color Blue = new Color(27, 77, 137);
        private static readonly Color BlueSoft = new Color(232, 240, 251);
        private static readonly Color Muted = new Color(92, 107, 122);
        private static readonly Color Border = new Color(221, 227, 234);
        private static readonly Color Zebra = new Color(250, 251, 253);

        private static readonly Color Green = new Color(26, 127, 55);
        private static readonly Color Amber = new Color(154, 103, 0);
        private static readonly Color Red = new Color(180, 35, 24);

        private const string FontName = "Arial";

        public static byte[] Build(KpiReportData data)
        {
            if (data == null) throw new ArgumentNullException("data");

            Document doc = CreateDocument(data);

            var renderer = new PdfDocumentRenderer(true);
            renderer.Document = doc;
            renderer.RenderDocument();

            using (var ms = new MemoryStream())
            {
                renderer.PdfDocument.Save(ms, false);
                return ms.ToArray();
            }
        }

        // ---------------------------------------------------------------
        private static Document CreateDocument(KpiReportData data)
        {
            var doc = new Document();
            doc.Info.Title = "HR KPI Monthly Report " + data.MonthLabel;
            doc.Info.Subject = "KPI summary for " + data.ScopeLabel;
            doc.Info.Author = "HR KPI Monitoring System";

            DefineStyles(doc);

            Section section = doc.AddSection();
            section.PageSetup.PageFormat = PageFormat.A4;
            section.PageSetup.Orientation = Orientation.Portrait;
            section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
            section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);
            section.PageSetup.LeftMargin = Unit.FromCentimeter(1.6);
            section.PageSetup.RightMargin = Unit.FromCentimeter(1.6);

            AddFooter(section);
            AddTitleBlock(section, data);
            AddSummaryBlock(section, data);
            AddKpiTable(section, data);

            if (data.HasDepartmentBreakdown)
                AddDepartmentTables(section, data);

            return doc;
        }

        private static void DefineStyles(Document doc)
        {
            Style normal = doc.Styles["Normal"];
            normal.Font.Name = FontName;
            normal.Font.Size = 9;
            normal.Font.Color = new Color(27, 36, 48);
            normal.ParagraphFormat.SpaceAfter = 0;

            Style title = doc.Styles.AddStyle("KpiTitle", "Normal");
            title.Font.Size = 18;
            title.Font.Bold = true;
            title.Font.Color = Navy;

            Style subtitle = doc.Styles.AddStyle("KpiSubtitle", "Normal");
            subtitle.Font.Size = 9.5;
            subtitle.Font.Color = Muted;

            Style heading = doc.Styles.AddStyle("KpiHeading", "Normal");
            heading.Font.Size = 12;
            heading.Font.Bold = true;
            heading.Font.Color = Navy;
            heading.ParagraphFormat.SpaceBefore = Unit.FromPoint(16);
            heading.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);

            Style footer = doc.Styles["Footer"];
            footer.Font.Size = 8;
            footer.Font.Color = Muted;
        }

        private static void AddFooter(Section section)
        {
            Paragraph p = section.Footers.Primary.AddParagraph();
            p.Format.Alignment = ParagraphAlignment.Center;
            p.Format.Borders.Top.Width = 0.5;
            p.Format.Borders.Top.Color = Border;
            p.Format.SpaceBefore = Unit.FromPoint(6);
            p.AddText("HR KPI Monitoring System    |    Page ");
            p.AddPageField();
            p.AddText(" of ");
            p.AddNumPagesField();
        }

        private static void AddTitleBlock(Section section, KpiReportData data)
        {
            Paragraph title = section.AddParagraph("HR KPI Monthly Report", "KpiTitle");
            title.Format.SpaceAfter = Unit.FromPoint(2);

            Paragraph line = section.AddParagraph(
                "Period: " + data.MonthLabel + "     |     Scope: " + data.ScopeLabel, "KpiSubtitle");
            line.Format.SpaceAfter = Unit.FromPoint(1);

            Paragraph gen = section.AddParagraph(
                "Generated: " + data.GeneratedAtText
                + "     |     By: " + (data.GeneratedBy ?? "-"), "KpiSubtitle");
            gen.Format.SpaceAfter = Unit.FromPoint(8);

            // เส้นคาดสีน้ำเงินคั่นหัวรายงานกับเนื้อหา
            Paragraph rule = section.AddParagraph();
            rule.Format.Borders.Bottom.Width = 2;
            rule.Format.Borders.Bottom.Color = Blue;
            rule.Format.SpaceAfter = Unit.FromPoint(10);
        }

        /// <summary>กล่องสรุป 4 ช่อง: จำนวน KPI ทั้งหมด / เขียว / เหลือง / แดง</summary>
        private static void AddSummaryBlock(Section section, KpiReportData data)
        {
            var table = new Table();
            table.Borders.Width = 0.75;
            table.Borders.Color = Border;
            table.Rows.LeftIndent = 0;

            Unit cellWidth = Unit.FromCentimeter(4.35);
            for (int i = 0; i < 4; i++)
                table.AddColumn(cellWidth).Format.Alignment = ParagraphAlignment.Center;

            Row labelRow = table.AddRow();
            labelRow.Shading.Color = BlueSoft;
            labelRow.Height = Unit.FromPoint(16);
            labelRow.VerticalAlignment = VerticalAlignment.Center;

            string[] labels = { "TOTAL KPIs", "ON TARGET", "WATCH", "BELOW TARGET" };
            for (int i = 0; i < labels.Length; i++)
            {
                Paragraph p = labelRow.Cells[i].AddParagraph(labels[i]);
                p.Format.Font.Size = 7.5;
                p.Format.Font.Bold = true;
                p.Format.Font.Color = Muted;
            }

            Row valueRow = table.AddRow();
            valueRow.Height = Unit.FromPoint(26);
            valueRow.VerticalAlignment = VerticalAlignment.Center;

            int[] values = { data.Rows.Count, data.CountGreen, data.CountYellow, data.CountRed };
            Color[] colors = { Navy, Green, Amber, Red };

            for (int i = 0; i < values.Length; i++)
            {
                Paragraph p = valueRow.Cells[i].AddParagraph(values[i].ToString());
                p.Format.Font.Size = 16;
                p.Format.Font.Bold = true;
                p.Format.Font.Color = colors[i];
            }

            section.Add(table);
        }

        // ---------------------------------------------------------------
        private static void AddKpiTable(Section section, KpiReportData data)
        {
            section.AddParagraph("KPI Detail", "KpiHeading");

            if (data.Rows.Count == 0)
            {
                Paragraph empty = section.AddParagraph("No KPI data available for this period.");
                empty.Format.Font.Color = Muted;
                empty.Format.Font.Italic = true;
                return;
            }

            var table = NewTable();
            table.AddColumn(Unit.FromCentimeter(5.6));   // KPI name
            table.AddColumn(Unit.FromCentimeter(1.7));   // Unit
            table.AddColumn(Unit.FromCentimeter(2.2));   // Actual
            table.AddColumn(Unit.FromCentimeter(2.2));   // Target
            table.AddColumn(Unit.FromCentimeter(2.0));   // Achievement
            table.AddColumn(Unit.FromCentimeter(1.9));   // MoM
            table.AddColumn(Unit.FromCentimeter(2.2));   // Status

            AddHeaderRow(table, new[] { "KPI", "Unit", "Actual", "Target", "Achv %", "MoM %", "Status" });

            int i = 0;
            foreach (var k in data.Rows)
            {
                Row row = table.AddRow();
                row.Height = Unit.FromPoint(15);
                row.VerticalAlignment = VerticalAlignment.Center;
                if (i % 2 == 1) row.Shading.Color = Zebra;

                Paragraph name = row.Cells[0].AddParagraph(k.KpiName ?? "");
                name.Format.Font.Bold = true;
                Paragraph code = row.Cells[0].AddParagraph(k.KpiCode ?? "");
                code.Format.Font.Size = 7;
                code.Format.Font.Color = Muted;

                Center(row.Cells[1], k.Unit ?? "-");
                Right(row.Cells[2], KpiReportData.Format(k.ActualValue, k.DecimalPlaces));
                Right(row.Cells[3], KpiReportData.Format(k.TargetValue, k.DecimalPlaces));
                Right(row.Cells[4], KpiReportData.FormatPct(k.AchievementPct));
                RightMoM(row.Cells[5], k.MoMChangePct, k.Direction);
                StatusCell(row.Cells[6], k.StatusFlag);

                i++;
            }

            section.Add(table);
        }

        private static void AddDepartmentTables(Section section, KpiReportData data)
        {
            section.AddParagraph("Breakdown by Department", "KpiHeading");

            // จัดกลุ่มตามแผนก โดยรักษาลำดับที่ repository ส่งมา
            var order = new List<string>();
            var grouped = new Dictionary<string, List<KpiDashboardRow>>();

            foreach (var r in data.DepartmentRows)
            {
                string dept = string.IsNullOrEmpty(r.DepartmentName) ? "(Unassigned)" : r.DepartmentName;
                if (!grouped.ContainsKey(dept))
                {
                    grouped[dept] = new List<KpiDashboardRow>();
                    order.Add(dept);
                }
                grouped[dept].Add(r);
            }

            foreach (string dept in order)
            {
                Paragraph head = section.AddParagraph(dept);
                head.Format.Font.Size = 10;
                head.Format.Font.Bold = true;
                head.Format.Font.Color = Blue;
                head.Format.SpaceBefore = Unit.FromPoint(12);
                head.Format.SpaceAfter = Unit.FromPoint(4);
                head.Format.KeepWithNext = true;

                var table = NewTable();
                table.AddColumn(Unit.FromCentimeter(6.3));   // KPI
                table.AddColumn(Unit.FromCentimeter(1.8));   // Unit
                table.AddColumn(Unit.FromCentimeter(2.4));   // Actual
                table.AddColumn(Unit.FromCentimeter(2.4));   // Target
                table.AddColumn(Unit.FromCentimeter(2.4));   // Achievement
                table.AddColumn(Unit.FromCentimeter(2.5));   // Status

                AddHeaderRow(table, new[] { "KPI", "Unit", "Actual", "Target", "Achv %", "Status" });

                int i = 0;
                foreach (var k in grouped[dept])
                {
                    Row row = table.AddRow();
                    row.Height = Unit.FromPoint(13);
                    row.VerticalAlignment = VerticalAlignment.Center;
                    if (i % 2 == 1) row.Shading.Color = Zebra;

                    row.Cells[0].AddParagraph(k.KpiName ?? "");
                    Center(row.Cells[1], k.Unit ?? "-");
                    Right(row.Cells[2], KpiReportData.Format(k.ActualValue, k.DecimalPlaces));
                    Right(row.Cells[3], KpiReportData.Format(k.TargetValue, k.DecimalPlaces));
                    Right(row.Cells[4], KpiReportData.FormatPct(k.AchievementPct));
                    StatusCell(row.Cells[5], k.StatusFlag);

                    i++;
                }

                section.Add(table);
            }
        }

        // ---------------------------------------------------------------
        // helper
        // ---------------------------------------------------------------
        private static Table NewTable()
        {
            var table = new Table();
            table.Borders.Width = 0.5;
            table.Borders.Color = Border;
            table.Rows.LeftIndent = 0;
            table.Format.Font.Size = 8.5;
            return table;
        }

        private static void AddHeaderRow(Table table, string[] headers)
        {
            Row row = table.AddRow();
            row.HeadingFormat = true;          // ซ้ำหัวตารางเมื่อขึ้นหน้าใหม่
            row.Shading.Color = Blue;
            row.Height = Unit.FromPoint(15);
            row.VerticalAlignment = VerticalAlignment.Center;

            for (int i = 0; i < headers.Length; i++)
            {
                Paragraph p = row.Cells[i].AddParagraph(headers[i]);
                p.Format.Font.Bold = true;
                p.Format.Font.Size = 8;
                p.Format.Font.Color = Colors.White;
                p.Format.Alignment = i == 0 ? ParagraphAlignment.Left : ParagraphAlignment.Center;
            }
        }

        private static void Center(Cell cell, string text)
        {
            cell.AddParagraph(text).Format.Alignment = ParagraphAlignment.Center;
        }

        private static void Right(Cell cell, string text)
        {
            cell.AddParagraph(text).Format.Alignment = ParagraphAlignment.Right;
        }

        /// <summary>
        /// MoM ต้องอ่านทิศทางของ KPI ด้วย: KPI ที่ยิ่งน้อยยิ่งดี (Direction = 'L')
        /// ค่าที่ลดลงคือดีขึ้น จึงต้องเป็นสีเขียวแม้ตัวเลขจะติดลบ
        /// </summary>
        private static void RightMoM(Cell cell, decimal? mom, string direction)
        {
            if (!mom.HasValue)
            {
                Paragraph none = cell.AddParagraph("-");
                none.Format.Alignment = ParagraphAlignment.Right;
                none.Format.Font.Color = Muted;
                return;
            }

            bool isImprovement = direction == "L" ? mom.Value < 0 : mom.Value > 0;
            string arrow = mom.Value >= 0 ? "+" : "-";

            Paragraph p = cell.AddParagraph(arrow + Math.Abs(mom.Value).ToString("N1", KpiReportData.ReportCulture) + "%");
            p.Format.Alignment = ParagraphAlignment.Right;
            p.Format.Font.Bold = true;
            p.Format.Font.Color = isImprovement ? Green : Red;
        }

        private static void StatusCell(Cell cell, string statusFlag)
        {
            string flag = (statusFlag ?? "").ToUpperInvariant();
            string text;
            Color color;

            switch (flag)
            {
                case "GREEN": text = "On Target"; color = Green; break;
                case "YELLOW": text = "Watch"; color = Amber; break;
                case "RED": text = "Below Target"; color = Red; break;
                default: text = "N/A"; color = Muted; break;
            }

            Paragraph p = cell.AddParagraph(text);
            p.Format.Alignment = ParagraphAlignment.Center;
            p.Format.Font.Bold = flag == "GREEN" || flag == "YELLOW" || flag == "RED";
            p.Format.Font.Color = color;
        }
    }
}
