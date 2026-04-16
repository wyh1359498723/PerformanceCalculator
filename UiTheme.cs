using System.Windows.Media;

namespace PerformanceCalculator2;

/// <summary>浅色 / 深色界面与图表用色板（WPF）。</summary>
internal sealed class UiTheme
{
    public Color Bg { get; }
    public Color Header { get; }
    public Color Accent { get; }
    public Color TextPrimary { get; }
    public Color TextMuted { get; }
    public Color GridHeader { get; }
    public Color GridAlt { get; }
    public Color GridLine { get; }
    public Color GridBg { get; }
    public Color GridSelectionBg { get; }
    public Color GridSelectionFg { get; }
    public Color DropZone { get; }
    public Color DropZoneHover { get; }
    public Color ChartSurface { get; }
    public Color ChartBorder { get; }
    public Color ChartGrid { get; }
    public Color ChartAxisLine { get; }
    public Color ChartTitleFg { get; }
    public Color ChartLegendFg { get; }
    public Color ChartColumnA { get; }
    public Color ChartColumnB { get; }
    public Color ChartLineOee { get; }
    public Color ChartBarOee { get; }
    public Color ChartLabelEff { get; }

    private UiTheme(
        Color bg, Color header, Color accent, Color textPrimary, Color textMuted,
        Color gridHeader, Color gridAlt, Color gridLine, Color gridBg,
        Color gridSelectionBg, Color gridSelectionFg,
        Color dropZone, Color dropZoneHover,
        Color chartSurface, Color chartBorder, Color chartGrid, Color chartAxisLine,
        Color chartTitleFg, Color chartLegendFg,
        Color chartColumnA, Color chartColumnB, Color chartLineOee, Color chartBarOee, Color chartLabelEff)
    {
        Bg = bg;
        Header = header;
        Accent = accent;
        TextPrimary = textPrimary;
        TextMuted = textMuted;
        GridHeader = gridHeader;
        GridAlt = gridAlt;
        GridLine = gridLine;
        GridBg = gridBg;
        GridSelectionBg = gridSelectionBg;
        GridSelectionFg = gridSelectionFg;
        DropZone = dropZone;
        DropZoneHover = dropZoneHover;
        ChartSurface = chartSurface;
        ChartBorder = chartBorder;
        ChartGrid = chartGrid;
        ChartAxisLine = chartAxisLine;
        ChartTitleFg = chartTitleFg;
        ChartLegendFg = chartLegendFg;
        ChartColumnA = chartColumnA;
        ChartColumnB = chartColumnB;
        ChartLineOee = chartLineOee;
        ChartBarOee = chartBarOee;
        ChartLabelEff = chartLabelEff;
    }

    public static UiTheme Light { get; } = new UiTheme(
        Color.FromRgb(245, 247, 250),
        Color.FromRgb(30, 58, 95),
        Color.FromRgb(59, 130, 246),
        Color.FromRgb(30, 41, 59),
        Color.FromRgb(100, 116, 139),
        Color.FromRgb(226, 232, 240),
        Color.FromRgb(248, 250, 252),
        Color.FromRgb(226, 232, 240),
        Colors.White,
        Color.FromRgb(219, 234, 254),
        Color.FromRgb(30, 41, 59),
        Color.FromRgb(238, 246, 255),
        Color.FromRgb(224, 242, 254),
        Colors.White,
        Color.FromRgb(226, 232, 240),
        Color.FromRgb(241, 245, 249),
        Color.FromRgb(203, 213, 225),
        Color.FromRgb(30, 41, 59),
        Color.FromRgb(51, 65, 85),
        Color.FromRgb(59, 130, 246),
        Color.FromRgb(45, 212, 191),
        Color.FromRgb(245, 158, 11),
        Color.FromRgb(14, 165, 233),
        Color.FromRgb(30, 64, 175));

    public static UiTheme Dark { get; } = new UiTheme(
        Color.FromRgb(15, 23, 42),
        Color.FromRgb(15, 23, 42),
        Color.FromRgb(59, 130, 246),
        Color.FromRgb(226, 232, 240),
        Color.FromRgb(148, 163, 184),
        Color.FromRgb(51, 65, 85),
        Color.FromRgb(30, 41, 59),
        Color.FromRgb(71, 85, 105),
        Color.FromRgb(30, 41, 59),
        Color.FromRgb(51, 65, 105),
        Color.FromRgb(241, 245, 249),
        Color.FromRgb(30, 41, 59),
        Color.FromRgb(51, 65, 85),
        Color.FromRgb(30, 41, 59),
        Color.FromRgb(71, 85, 105),
        Color.FromRgb(51, 65, 85),
        Color.FromRgb(100, 116, 139),
        Color.FromRgb(241, 245, 249),
        Color.FromRgb(203, 213, 225),
        Color.FromRgb(96, 165, 250),
        Color.FromRgb(45, 212, 191),
        Color.FromRgb(251, 191, 36),
        Color.FromRgb(56, 189, 248),
        Color.FromRgb(191, 219, 254));

    public static SolidColorBrush Solid(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
