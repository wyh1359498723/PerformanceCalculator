using System.Globalization;
using ClosedXML.Excel;

namespace PerformanceCalculator2;

public static class ExcelPerformanceReader
{
    private const string TokenTestTime = "单片测试时间";
    private const string TokenActualTime = "单片实际用时";
    private const string TokenOperator = "操作员";
    private const string TokenWorkday = "工作日";

    public static IReadOnlyList<TestRecord> ReadWorkbook(string path)
    {
        var list = new List<TestRecord>();
        using (var wb = new XLWorkbook(path))
        {
            foreach (var ws in wb.Worksheets)
                ReadWorksheet(ws, list);
        }
        return list;
    }

    private static void ReadWorksheet(IXLWorksheet ws, List<TestRecord> list)
    {
        if (ws.LastRowUsed() == null) return;
        if (!TryBuildColumnMapFromFirstRow(ws, out var map, out _))
            return;

        int lastRow = ws.LastRowUsed().RowNumber();
        for (int r = 2; r <= lastRow; r++)
        {
            var rpNo = GetString(ws.Cell(r, map.RpNo)).Trim();
            if (!string.Equals(rpNo, "RP0", StringComparison.OrdinalIgnoreCase))
                continue;

            var op = GetString(ws.Cell(r, map.Operator));
            if (string.IsNullOrWhiteSpace(op)) continue;

            double testTime = GetDouble(ws.Cell(r, map.TestTime));
            double actualTime = GetDouble(ws.Cell(r, map.ActualTime));
            if (actualTime <= 0) actualTime = testTime;

            var date = GetDate(ws.Cell(r, map.Workday));
            string monthKey = date.HasValue
                ? date.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture)
                : "";

            list.Add(new TestRecord
            {
                RpNo = rpNo,
                TestTimeMinutes = testTime,
                ActualTimeMinutes = actualTime,
                OperatorName = op.Trim(),
                TestDate = date,
                MonthKey = monthKey
            });
        }
    }

    private static bool TryBuildColumnMapFromFirstRow(IXLWorksheet ws, out ColumnMap map, out string? error)
    {
        map = default;
        error = null;
        var row = ws.Row(1);
        var lastCell = row.LastCellUsed();
        if (lastCell == null)
        {
            error = "首行无表头";
            return false;
        }

        int lastCol = lastCell.Address.ColumnNumber;
        int? colTest = null, colRp = null, colAct = null, colOp = null, colDay = null;

        for (int c = 1; c <= lastCol; c++)
        {
            var h = GetString(ws.Cell(1, c)).Trim();
            if (string.IsNullOrEmpty(h)) continue;

            if (colTest == null && Contains(h, TokenTestTime))
            {
                colTest = c;
                continue;
            }
            if (colAct == null && Contains(h, TokenActualTime))
            {
                colAct = c;
                continue;
            }
            if (colOp == null && Contains(h, TokenOperator))
            {
                colOp = c;
                continue;
            }
            if (colDay == null && Contains(h, TokenWorkday))
            {
                colDay = c;
                continue;
            }
            if (colRp == null && HeaderMatchesRpNo(h))
                colRp = c;
        }

        if (colTest == null || colRp == null || colAct == null || colOp == null || colDay == null)
        {
            error = "表头缺少列（需包含：单片测试时间、RP_NO、单片实际用时、操作员、工作日）";
            return false;
        }

        map = new ColumnMap(colTest.Value, colRp.Value, colAct.Value, colOp.Value, colDay.Value);
        return true;
    }

    private static bool Contains(string header, string token) =>
        header.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool HeaderMatchesRpNo(string header)
    {
        if (string.IsNullOrEmpty(header)) return false;
        if (header.IndexOf("RP_NO", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        int i = header.IndexOf("RP", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return false;
        int j = header.IndexOf("NO", i + 2, StringComparison.OrdinalIgnoreCase);
        return j > i;
    }

    private readonly struct ColumnMap
    {
        public ColumnMap(int testTime, int rpNo, int actualTime, int op, int workday)
        {
            TestTime = testTime;
            RpNo = rpNo;
            ActualTime = actualTime;
            Operator = op;
            Workday = workday;
        }

        public int TestTime { get; }
        public int RpNo { get; }
        public int ActualTime { get; }
        public int Operator { get; }
        public int Workday { get; }
    }

    private static string GetString(IXLCell cell)
    {
        try
        {
            if (cell.IsEmpty()) return "";
            var v = cell.GetString();
            return v ?? "";
        }
        catch
        {
            return cell.Value.ToString()?.Trim() ?? "";
        }
    }

    private static double GetDouble(IXLCell cell)
    {
        if (cell.IsEmpty()) return 0;
        if (cell.DataType == XLDataType.Number)
            return cell.GetDouble();
        if (cell.DataType == XLDataType.Text)
        {
            var s = cell.GetString().Trim();
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return d;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out d))
                return d;
        }
        try
        {
            return cell.GetDouble();
        }
        catch
        {
            return 0;
        }
    }

    private static DateTime? GetDate(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime();
        if (cell.DataType == XLDataType.Number)
        {
            try
            {
                return DateTime.FromOADate(cell.GetDouble());
            }
            catch
            {
                return null;
            }
        }
        var s = GetString(cell);
        if (string.IsNullOrWhiteSpace(s)) return null;
        string[] formats =
        {
            "yyyy/M/d", "yyyy/M/dd", "yyyy/MM/d", "yyyy/MM/dd",
            "yyyy-M-d", "yyyy-M-dd", "yyyy-MM-d", "yyyy-MM-dd",
            "M/d/yyyy", "MM/dd/yyyy", "d/M/yyyy", "dd/MM/yyyy"
        };
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;
        if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
            return dt;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return dt;
        return null;
    }
}
