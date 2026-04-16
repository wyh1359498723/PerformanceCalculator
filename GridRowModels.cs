namespace PerformanceCalculator2;

public sealed class OperatorGridRow
{
    public string OperatorName { get; init; } = "";
    public string EffectiveMinutes { get; init; } = "";
    public string ActualMinutes { get; init; } = "";
    public string Oee { get; init; } = "";
    public string Contribution { get; init; } = "";
}

public sealed class MonthGridRow
{
    public string MonthKey { get; init; } = "";
    public string TotalEffective { get; init; } = "";
    public string TotalActual { get; init; } = "";
    public string Oee { get; init; } = "";
}
