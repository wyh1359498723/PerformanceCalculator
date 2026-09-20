using ClosedXML.Excel;

namespace PerformanceCalculator2;

/// <summary>将指定月份的操作员汇总导出为 .xlsx（与主界面绩效表列一致）。</summary>
public static class MonthlyPerformanceExporter
{
    public static void ExportToXlsx(string path, string monthKey, IReadOnlyList<OperatorStats> operators, double avgEffectivePerOperator)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("当月绩效");

        ws.Cell(1, 1).Value = "当月绩效表";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        ws.Cell(2, 1).Value = "月份";
        ws.Cell(2, 2).Value = monthKey;
        ws.Cell(3, 1).Value = "平均有效工时（分，总有效÷有记录人数）";
        ws.Cell(3, 2).Value = avgEffectivePerOperator;
        ws.Cell(3, 2).Style.NumberFormat.Format = "0.##";
        ws.Cell(4, 1).Value = "操作员人数";
        ws.Cell(4, 2).Value = operators.Count;

        const int headerRow = 6;
        string[] headers = ["操作员", "有效工时(分)", "实际工时(分)", "OEE", "产出贡献占比"];
        for (int c = 0; c < headers.Length; c++)
        {
            var h = ws.Cell(headerRow, c + 1);
            h.Value = headers[c];
            h.Style.Font.Bold = true;
            h.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
            h.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        int r = headerRow + 1;
        foreach (var o in operators)
        {
            ws.Cell(r, 1).Value = o.OperatorName;
            ws.Cell(r, 2).Value = o.EffectiveMinutes;
            ws.Cell(r, 2).Style.NumberFormat.Format = "0.##";
            ws.Cell(r, 3).Value = o.ActualMinutes;
            ws.Cell(r, 3).Style.NumberFormat.Format = "0.##";
            if (o.Oee.HasValue)
            {
                ws.Cell(r, 4).Value = o.Oee.Value;
                ws.Cell(r, 4).Style.NumberFormat.Format = "0.00%";
            }
            else
                ws.Cell(r, 4).Value = "—";

            ws.Cell(r, 5).Value = o.ContributionRatio;
            ws.Cell(r, 5).Style.NumberFormat.Format = "0.00%";
            r++;
        }

        ws.Columns(1, 5).AdjustToContents();
        ws.SheetView.FreezeRows(headerRow);
        wb.SaveAs(path);
    }
}
