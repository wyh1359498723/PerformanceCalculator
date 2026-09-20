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
        const double titleH = 38;
        const double axisBottom = 30;
        const double padL = 10;
        const double padR = 10;
        double plotTop = titleH + 2;
        double plotBottom = h - axisBottom - 4;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var titleBrush = UiTheme.Solid(t.ChartTitleFg);
        var titleTypeface = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var titleFt = new FormattedText(TitleText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, titleTypeface, 14, titleBrush, null, TextFormattingMode.Display, pixelsPerDip);
        dc.DrawText(titleFt, new Point(padL + 2, 8));
        var titleLinePen = new Pen(UiTheme.Solid(t.ChartBorder), 1);
        if (titleLinePen.CanFreeze) titleLinePen.Freeze();
        dc.DrawLine(titleLinePen, new Point(0, 36), new Point(w, 36));

        if (_rows.Count == 0 || plotBottom <= plotTop)
        {
            var f = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var ft = new FormattedText("(无数据)", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, f, 11, UiTheme.Solid(t.ChartLegendFg), null, TextFormattingMode.Display, pixelsPerDip);
            dc.DrawText(ft, new Point(padL, plotTop + 8));
            return;
        }

        double plotH = plotBottom - plotTop;
        double rowH = plotH / _rows.Count;
        double maxOee = Math.Max(1.0, _rows.Max(x => x.Oee ?? 0));

        double axisX0 = padL + 78;
        double axisX1 = w - padR - 54;
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
            dc.DrawLine(penGrid, new Point(x, plotTop), new Point(x, axisYPos));
            dc.DrawLine(penAxis, new Point(x, axisYPos), new Point(x, axisYPos + 4));
            string sv = v.ToString("P0", CultureInfo.CurrentCulture);
            var tickFt = new FormattedText(sv, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tickTypeface, 10, tickBrush, null, TextFormattingMode.Display, pixelsPerDip);
            dc.DrawText(tickFt, new Point(x - tickFt.Width * 0.5, axisYPos + 5));
        }

        Brush barBrush;
        if (ReferenceEquals(t, UiTheme.Tech))
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            };
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(66, 165, 255), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(69, 229, 220), 1));
            if (gradient.CanFreeze) gradient.Freeze();
            barBrush = gradient;
        }
        else
            barBrush = UiTheme.Solid(t.ChartBarOee);
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

            string name = s.OperatorName ?? "";
            var nameFt = new FormattedText(name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, nameTypeface, 11, nameBrush, null, TextFormattingMode.Display, pixelsPerDip)
            {
                MaxTextWidth = axisX0 - padL - 10,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };
            dc.DrawText(nameFt, new Point(axisX0 - 8 - nameFt.Width, plotTop + i * rowH + (rowH - nameFt.Height) * 0.5));

            string valueText = oee.ToString("P1", CultureInfo.CurrentCulture);
            var valueFt = new FormattedText(valueText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, nameTypeface, 10.5, nameBrush, null, TextFormattingMode.Display, pixelsPerDip);
            double valueX = Math.Min(axisX0 + barLen + 7, w - padR - valueFt.Width);
            dc.DrawText(valueFt, new Point(valueX, plotTop + i * rowH + (rowH - valueFt.Height) * 0.5));
        }

        var axisLabelTypeface = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var axisLabelFt = new FormattedText("OEE →", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, axisLabelTypeface, 11, tickBrush, null, TextFormattingMode.Display, pixelsPerDip);
        dc.DrawText(axisLabelFt, new Point(axisX0 + (axisX1 - axisX0) * 0.5 - 24, axisYPos + 20));
    }
}
