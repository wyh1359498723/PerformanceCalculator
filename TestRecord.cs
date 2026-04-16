namespace PerformanceCalculator2;

/// <summary>从 Excel 单行解析出的测试记录。</summary>
public sealed class TestRecord
{
    public string RpNo { get; set; } = "";
    public double TestTimeMinutes { get; set; }
    public double ActualTimeMinutes { get; set; }
    public string OperatorName { get; set; } = "";
    public DateTime? TestDate { get; set; }
    public string MonthKey { get; set; } = "";
}
