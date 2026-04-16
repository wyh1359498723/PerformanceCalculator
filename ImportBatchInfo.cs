namespace PerformanceCalculator2;

public sealed class ImportBatchInfo
{
    public long Id { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string MonthKey { get; set; } = "";
    public string SourceFiles { get; set; } = "";
    public int RowCount { get; set; }
}
