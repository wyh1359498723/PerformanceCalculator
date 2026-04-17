using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PerformanceCalculator2;

/// <summary>自绘「操作员 × OEE」横向绩效条（WPF）；仅展示有效 OEE 前十名。</summary>
internal sealed class OperatorOeeControl : FrameworkElement
{
    private const int TopCount = 10;
    private const string TitleText = "各操作员 OEE 前十名";
    private readonly List<OperatorStats> _rows = new();
    private UiTheme _theme = UiTheme.Light;

    public void Bind(IReadOnlyList<OperatorStats> stats, UiTheme theme)
    {
        _theme = theme;
        _rows.Clear();
        if (stats is { Count: > 0 })
        {
            var top = stats.Where(s => s.Oee.HasValue)
                .OrderByDescending(s => s.Oee!.Value)
                .Take(TopCount)
                .ToList();
            _rows.AddRange(top);
        }
        InvalidateVisual();
    }

    public void ApplyTheme(UiTheme theme)
    {
        _theme = theme;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var t = _theme;
        dc.DrawRectangle(UiTheme.Solid(t.ChartSurface), null, new Rect(0, 0, ActualWidth, ActualHeight));

        double w = ActualWidth;
        double h = ActualHeight;
        const double titleH = 30;
        const double axisBottom = 30;
        const double padL = 10;
        const double padR = 10;
        double plotTop = titleH + 2;
        double plotBottom = h - axisBottom - 4;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var titleBrush = UiTheme.Solid(t.ChartTitleFg);
        var titleTypeface = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var titleFt = new FormattedText(TitleText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, titleTypeface, 12, titleBrush, null, TextFormattingMode.Display, pixelsPerDip);
        dc.DrawText(titleFt, new Point(padL, 6));
        var accentPen = new Pen(new SolidColorBrush(Color.FromArgb(200, t.Accent.R, t.Accent.G, t.Accent.B)), 2);
        if (accentPen.CanFreeze) accentPen.Freeze();
        dc.DrawLine(accentPen, new Point(padL, 26), new Point(Math.Min(w - padR, padL + 240), 26));

        if (_rows.Count == 0 || plotBottom <= plotTop)
        {
            var f = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var ft = new FormattedText("(无数据)", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, f, 11, UiTheme.Solid(t.ChartLegendFg), null, TextFormattingMode.Display, pixelsPerDip);
            dc.DrawText(ft, new Point(padL, plotTop + 8));
            return;
        }

        double plotH = plotBottom - plotTop;
        double rowH = plotH / _rows.Count;
        double maxOee = _rows.Max(x => x.Oee ?? 0);
        if (maxOee < 1e-12) maxOee = 1;

        double axisX0 = padL + 52;
        double nameReserve = Math.Max(100, Math.Min(200, w * 0.24));
        double axisX1 = w - padR - nameReserve;
        if (axisX1 <= axisX0 + 40)
            axisX1 = axisX0 + 40;

        double axisYPos = plotBottom;

        var penAxis = new Pen(UiTheme.Solid(t.ChartAxisLine), 1.2);
        if (penAxis.CanFreeze) penAxis.Freeze();
        var penGrid = new Pen(UiTheme.Solid(t.ChartGrid), 1) { DashStyle = DashStyles.Dot };
        if (penGrid.CanFreeze) penGrid.Freeze();

        dc.DrawLine(penAxis, new Point(axisX0, plotTop - 2), new Point(axisX0, axisYPos));
        dc.DrawLine(penAxis, new Point(axisX0, axisYPos), new Point(axisX1, axisYPos));

        const int tickCount = 5;
        var tickTypeface = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var tickBrush = UiTheme.Solid(t.ChartLegendFg);
        for (int ti = 0; ti <= tickCount; ti++)
        {
            double v = maxOee * ti / tickCount;
            double x = axisX0 + ti / (double)tickCount * (axisX1 - axisX0);
            dc.DrawLine(penAxis, new Point(x, axisYPos), new Point(x, axisYPos + 4));
            string sv = v.ToString("0.####", CultureInfo.CurrentCulture);
            var tickFt = new FormattedText(sv, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tickTypeface, 10, tickBrush, null, TextFormattingMode.Display, pixelsPerDip);
            dc.DrawText(tickFt, new Point(x - tickFt.Width * 0.5, axisYPos + 5));
        }

        var barBrush = UiTheme.Solid(t.ChartBarOee);
        var barPen = new Pen(new SolidColorBrush(Color.FromRgb(
            (byte)Math.Max(0, t.ChartBarOee.R - 35),
            (byte)Math.Max(0, t.ChartBarOee.G - 25),
            t.ChartBarOee.B)), 1);
        if (barPen.Brush is SolidColorBrush sb && sb.CanFreeze) sb.Freeze();
        if (barPen.CanFreeze) barPen.Freeze();

        var nameTypeface = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var nameBrush = UiTheme.Solid(t.ChartTitleFg);

        for (int i = 0; i < _rows.Count; i++)
        {
            var s = _rows[i];
            double oee = s.Oee ?? 0;
            double cy = plotTop + i * rowH + rowH * 0.5;
            double barH = Math.Min(rowH * 0.58, 26);
            double y0 = cy - barH * 0.5;
            double fullW = axisX1 - axisX0;
            double barLen = fullW * (oee / maxOee);
            if (barLen < 2) barLen = 2;

            dc.DrawRectangle(barBrush, barPen, new Rect(axisX0, y0, barLen, barH));
            dc.DrawLine(penGrid, new Point(axisX0, cy), new Point(axisX1, cy));

            string name = s.OperatorName ?? "";
            double nameX = axisX0 + barLen + 6;
            double avail = w - padR - nameX;
            if (avail < 20) avail = 20;
            var nameFt = new FormattedText(name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, nameTypeface, 11, nameBrush, null, TextFormattingMode.Display, pixelsPerDip)
            {
                MaxTextWidth = avail,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };
            dc.DrawText(nameFt, new Point(nameX, plotTop + i * rowH + (rowH - nameFt.Height) * 0.5));
        }

        var axisLabelTypeface = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var axisLabelFt = new FormattedText("OEE →", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, axisLabelTypeface, 11, tickBrush, null, TextFormattingMode.Display, pixelsPerDip);
        dc.DrawText(axisLabelFt, new Point(axisX0 + (axisX1 - axisX0) * 0.5 - 24, axisYPos + 20));
    }
}
