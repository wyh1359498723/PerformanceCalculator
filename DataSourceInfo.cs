namespace PerformanceCalculator2;

/// <summary>一个已导入的 Excel 数据源及其明细数量。</summary>
public sealed class DataSourceInfo
{
    public long Id { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string MonthKey { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public int RowCount { get; set; }
}
