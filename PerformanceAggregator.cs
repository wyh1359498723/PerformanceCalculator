using System.Globalization;

namespace PerformanceCalculator2;

public static class PerformanceMetrics
{
    /// <summary>实际工时（分）低于此阈值时，不计算 OEE，视为无效。</summary>
    public const double MinActualMinutesForOee = 1e-3;
}

public sealed class OperatorStats
{
    public string OperatorName { get; set; } = "";
    public double EffectiveMinutes { get; set; }
    public double ActualMinutes { get; set; }
    public double? Oee => ActualMinutes > PerformanceMetrics.MinActualMinutesForOee
        ? EffectiveMinutes / ActualMinutes
        : (double?)null;
    public double ContributionRatio { get; set; }
}

public sealed class MonthStats
{
    public string MonthKey { get; set; } = "";
    public double TotalEffectiveMinutes { get; set; }
    public double TotalActualMinutes { get; set; }
    public double? Oee => TotalActualMinutes > PerformanceMetrics.MinActualMinutesForOee
        ? TotalEffectiveMinutes / TotalActualMinutes
        : (double?)null;
}

public static class PerformanceAggregator
{
    public static IReadOnlyList<OperatorStats> AggregateByOperator(IEnumerable<TestRecord> records)
    {
        var groups = records
            .GroupBy(r => r.OperatorName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new OperatorStats
            {
                OperatorName = g.Key,
                EffectiveMinutes = g.Sum(x => x.TestTimeMinutes),
                ActualMinutes = g.Sum(x => x.ActualTimeMinutes)
            })
            .ToList();

        double totalEff = groups.Sum(x => x.EffectiveMinutes);
        foreach (var o in groups)
            o.ContributionRatio = totalEff > 1e-9 ? o.EffectiveMinutes / totalEff : 0;

        return groups
            .OrderByDescending(o => o.Oee.HasValue)
            .ThenByDescending(o => o.Oee ?? 0)
            .ThenBy(o => o.OperatorName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<MonthStats> AggregateByMonth(IEnumerable<TestRecord> records)
    {
        return records
            .Where(r => !string.IsNullOrEmpty(r.MonthKey))
            .GroupBy(r => r.MonthKey, StringComparer.Ordinal)
            .Select(g => new MonthStats
            {
                MonthKey = g.Key,
                TotalEffectiveMinutes = g.Sum(x => x.TestTimeMinutes),
                TotalActualMinutes = g.Sum(x => x.ActualTimeMinutes)
            })
            .OrderBy(m => m.MonthKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>平均有效工时 = 总有效工时 / 有记录的操作员人数（去重）。</summary>
    public static double AverageEffectiveMinutesPerOperator(IEnumerable<TestRecord> records)
    {
        var list = records.ToList();
        if (list.Count == 0) return 0;
        int people = list.Select(r => r.OperatorName).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (people == 0) return 0;
        return list.Sum(x => x.TestTimeMinutes) / people;
    }

    public static string FormatMinutes(double minutes) =>
        minutes.ToString("0.##", CultureInfo.CurrentCulture);

    public static string FormatPercent(double ratio) =>
        (ratio * 100).ToString("0.##", CultureInfo.CurrentCulture) + "%";

    public static string FormatOee(double? oee) =>
        !oee.HasValue ? "—" : oee.Value.ToString("0.####", CultureInfo.CurrentCulture);
}
