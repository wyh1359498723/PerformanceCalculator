using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace PerformanceCalculator2;

public partial class MainWindow : Window
{
    private readonly HistoryRepository _repo = new();
    private readonly SemaphoreSlim _dataWorkLock = new(1, 1);
    private readonly ObservableCollection<string> _perfMonthItems = new();
    private readonly ObservableCollection<SelectableMonth> _chartMonthItems = new();
    private readonly ObservableCollection<OperatorGridRow> _operatorRows = new();
    private readonly ObservableCollection<MonthGridRow> _monthRows = new();

    private bool _suppressMonthUi;
    private const int ImportProgressStripHeight = 42;
    private double _panelTopIdleHeight;

    private UiTheme Theme => ChkDarkMode.IsChecked == true ? UiTheme.Dark : UiTheme.Light;

    public MainWindow()
    {
        InitializeComponent();
        CboPerfMonth.ItemsSource = _perfMonthItems;
        ChartMonthItemsControl.ItemsSource = _chartMonthItems;
        DgvOperators.ItemsSource = _operatorRows;
        DgvMonths.ItemsSource = _monthRows;
        SetupDataGrids();
        ApplyChrome();
        StyleDataGrid(DgvOperators);
        StyleDataGrid(DgvMonths);
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        _panelTopIdleHeight = PanelTop.ActualHeight > 0 ? PanelTop.ActualHeight : 96;
        RepopulateMonthUis(selectLatestTableMonth: true);
        RefreshViews();
    }

    private void SetupDataGrids()
    {
        DgvOperators.Columns.Clear();
        DgvOperators.Columns.Add(new DataGridTextColumn { Header = "操作员", Binding = new Binding(nameof(OperatorGridRow.OperatorName)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        DgvOperators.Columns.Add(new DataGridTextColumn { Header = "有效工时(分)", Binding = new Binding(nameof(OperatorGridRow.EffectiveMinutes)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        DgvOperators.Columns.Add(new DataGridTextColumn { Header = "实际工时(分)", Binding = new Binding(nameof(OperatorGridRow.ActualMinutes)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        DgvOperators.Columns.Add(new DataGridTextColumn { Header = "OEE", Binding = new Binding(nameof(OperatorGridRow.Oee)), Width = new DataGridLength(80) });
        DgvOperators.Columns.Add(new DataGridTextColumn { Header = "产出贡献占比", Binding = new Binding(nameof(OperatorGridRow.Contribution)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        DgvMonths.Columns.Clear();
        DgvMonths.Columns.Add(new DataGridTextColumn { Header = "月份", Binding = new Binding(nameof(MonthGridRow.MonthKey)), Width = new DataGridLength(80) });
        DgvMonths.Columns.Add(new DataGridTextColumn { Header = "总有效工时(分)", Binding = new Binding(nameof(MonthGridRow.TotalEffective)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        DgvMonths.Columns.Add(new DataGridTextColumn { Header = "总实际工时(分)", Binding = new Binding(nameof(MonthGridRow.TotalActual)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        DgvMonths.Columns.Add(new DataGridTextColumn { Header = "OEE", Binding = new Binding(nameof(MonthGridRow.Oee)), Width = new DataGridLength(80) });
    }

    private void RepopulateMonthUis(bool selectLatestTableMonth)
    {
        var months = _repo.ListDistinctMonths().OrderByDescending(m => m, StringComparer.Ordinal).ToList();

        _suppressMonthUi = true;
        try
        {
            _perfMonthItems.Clear();
            foreach (var m in months)
                _perfMonthItems.Add(m);

            _chartMonthItems.Clear();
            foreach (var m in months.OrderBy(x => x, StringComparer.Ordinal))
                _chartMonthItems.Add(new SelectableMonth(m));

            if (months.Count == 0)
            {
                CboPerfMonth.SelectedItem = null;
            }
            else if (selectLatestTableMonth)
            {
                var latest = _repo.GetLatestImportMonthKey();
                if (!string.IsNullOrEmpty(latest) && months.Contains(latest!))
                    CboPerfMonth.SelectedItem = latest;
                else
                    CboPerfMonth.SelectedIndex = 0;
            }
            else
            {
                var cur = CboPerfMonth.SelectedItem as string;
                if (!string.IsNullOrEmpty(cur) && months.Contains(cur!))
                    CboPerfMonth.SelectedItem = cur;
                else
                    CboPerfMonth.SelectedIndex = months.Count > 0 ? 0 : -1;
            }
        }
        finally
        {
            _suppressMonthUi = false;
        }
    }

    private HashSet<string> GetSelectedChartMonths()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in _chartMonthItems)
        {
            if (item.IsSelected && !string.IsNullOrEmpty(item.MonthKey))
                set.Add(item.MonthKey);
        }
        return set;
    }

    private void ChartMonthCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressMonthUi) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(RefreshViews));
    }

    private void CboPerfMonth_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMonthUi) return;
        RefreshViews();
    }

    private void ChkBelowAvg_Changed(object sender, RoutedEventArgs e) => RefreshViews();

    private void BtnHistory_Click(object sender, RoutedEventArgs e)
    {
        var w = new HistoryWindow(_repo) { Owner = this };
        w.ShowDialog();
        RepopulateMonthUis(selectLatestTableMonth: false);
        RefreshViews();
    }

    private void ChkDarkMode_Changed(object sender, RoutedEventArgs e)
    {
        ApplyChrome();
        StyleDataGrid(DgvOperators);
        StyleDataGrid(DgvMonths);
        OperatorOeeChart.ApplyTheme(Theme);
        if (PlotMonth.Model != null)
            ApplyPlotTheme(PlotMonth.Model, Theme);
        RefreshViews();
    }

    private void ApplyChrome()
    {
        var t = Theme;
        Background = UiTheme.Solid(t.Bg);
        Foreground = UiTheme.Solid(t.TextPrimary);
        MainGrid.Background = UiTheme.Solid(t.Bg);

        PanelTop.Background = UiTheme.Solid(t.Header);
        LblPath.Foreground = Brushes.White;

        StyleHeaderButton(BtnSelectFiles);
        StyleHeaderButton(BtnExportOperatorChart);
        StyleHeaderButton(BtnExportMonthChart);

        ChkDarkMode.Foreground = Brushes.White;
        ChkDarkMode.Background = Brushes.Transparent;

        PanelDrop.Background = UiTheme.Solid(t.DropZone);
        PanelDrop.BorderBrush = UiTheme.Solid(t.GridLine);
        LblDropHint.Foreground = UiTheme.Solid(t.TextMuted);

        PanelImportProgress.Background = UiTheme.Solid(t.Header);
        LblImportDetail.Foreground = Brushes.White;

        PanelPerf.Background = UiTheme.Solid(t.Bg);
        CboPerfMonth.Background = UiTheme.Solid(t.GridBg);
        CboPerfMonth.Foreground = UiTheme.Solid(t.TextPrimary);
        LblAvgEffective.Foreground = UiTheme.Solid(t.TextPrimary);
        ChkBelowAvg.Foreground = UiTheme.Solid(t.TextPrimary);
        StyleHeaderButton(BtnHistory);

        PlotMonth.Background = UiTheme.Solid(t.ChartSurface);
    }

    private void StyleHeaderButton(Button b)
    {
        b.Background = UiTheme.Solid(Theme.Accent);
        b.Foreground = Brushes.White;
        b.FontWeight = System.Windows.FontWeights.Bold;
        b.Padding = new Thickness(10, 4, 10, 4);
        b.Cursor = Cursors.Hand;
        b.BorderThickness = new Thickness(0);
    }

    private void StyleDataGrid(DataGrid dg)
    {
        var t = Theme;
        dg.Background = UiTheme.Solid(t.GridBg);
        dg.BorderThickness = new Thickness(0);
        dg.HorizontalGridLinesBrush = UiTheme.Solid(t.GridLine);
        dg.RowBackground = UiTheme.Solid(t.GridBg);
        dg.AlternatingRowBackground = UiTheme.Solid(t.GridAlt);
        dg.Foreground = UiTheme.Solid(t.TextPrimary);
        dg.VerticalGridLinesBrush = Brushes.Transparent;

        var headerStyle = new Style(typeof(DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(BackgroundProperty, UiTheme.Solid(t.GridHeader)));
        headerStyle.Setters.Add(new Setter(ForegroundProperty, UiTheme.Solid(t.TextPrimary)));
        headerStyle.Setters.Add(new Setter(FontWeightProperty, System.Windows.FontWeights.Bold));
        headerStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(8, 6, 8, 6)));
        dg.ColumnHeaderStyle = headerStyle;

        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(8, 4, 8, 4)));
        cellStyle.Setters.Add(new Setter(ForegroundProperty, UiTheme.Solid(t.TextPrimary)));
        cellStyle.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        cellStyle.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
        var selectedTrigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(BackgroundProperty, UiTheme.Solid(t.GridSelectionBg)));
        selectedTrigger.Setters.Add(new Setter(ForegroundProperty, UiTheme.Solid(t.GridSelectionFg)));
        cellStyle.Triggers.Add(selectedTrigger);
        dg.CellStyle = cellStyle;
    }

    private void BtnSelectFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) != true) return;
        _ = LoadExcelFilesAsync(dlg.FileNames);
    }

    private void PanelDrop_PreviewDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void PanelDrop_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            PanelDrop.Background = UiTheme.Solid(Theme.DropZoneHover);
        }
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void PanelDrop_DragLeave(object sender, DragEventArgs e) =>
        PanelDrop.Background = UiTheme.Solid(Theme.DropZone);

    private void PanelDrop_Drop(object sender, DragEventArgs e)
    {
        PanelDrop.Background = UiTheme.Solid(Theme.DropZone);
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var xlsx = paths.Where(p => p.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (xlsx.Length == 0)
        {
            MessageBox.Show(this, "请拖放 .xlsx 文件。", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _ = LoadExcelFilesAsync(xlsx);
    }

    private void SetImportUiBusy(bool busy)
    {
        BtnSelectFiles.IsEnabled = !busy;
        PanelDrop.IsHitTestVisible = !busy;
        ChartMonthItemsControl.IsEnabled = !busy;
        CboPerfMonth.IsEnabled = !busy;
        ChkBelowAvg.IsEnabled = !busy;
        BtnHistory.IsEnabled = !busy;
    }

    private static (List<TestRecord> merged, List<string> errors, Exception? insertEx) ImportExcelFilesCore(
        string[] pathsCopy,
        HistoryRepository repo,
        IProgress<(int Percent, string Message)>? phaseProgress)
    {
        void Report(int p, string msg)
        {
            if (p < 0) p = 0;
            if (p > 100) p = 100;
            phaseProgress?.Report((p, msg));
        }

        var merged = new List<TestRecord>();
        var errors = new List<string>();
        int nFiles = pathsCopy.Length;
        if (nFiles == 0)
        {
            Report(0, "未选择文件");
            return (merged, errors, null);
        }

        for (int fi = 0; fi < nFiles; fi++)
        {
            string path = pathsCopy[fi];
            int pctRead = (int)(40.0 * fi / nFiles);
            Report(pctRead, $"【读文件】({fi + 1}/{nFiles}) {Path.GetFileName(path)}");
            try
            {
                if (!File.Exists(path))
                    continue;
                var part = ExcelPerformanceReader.ReadWorkbook(path);
                merged.AddRange(part);
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        if (merged.Count == 0)
        {
            Report(0, errors.Count > 0 ? "未能读取到有效数据" : "未选择有效文件");
            return (merged, errors, null);
        }

        IProgress<(int current, int total)>? rowProgress = null;
        if (phaseProgress != null)
        {
            rowProgress = new Progress<(int current, int total)>(t =>
            {
                int n = t.total;
                int c = t.current;
                int p = n <= 0 ? 40 : (int)(40 + 59.0 * c / n);
                if (p > 99) p = 99;
                phaseProgress.Report((p, $"【写库】{c:N0} / {n:N0} 条"));
            });
        }

        try
        {
            repo.InsertImport(
                merged,
                string.Join("; ", pathsCopy.Select(Path.GetFileName)),
                rowProgress);
            Report(100, $"【写库】完成，共 {merged.Count:N0} 条");
            return (merged, errors, null);
        }
        catch (Exception ex)
        {
            Report(0, "写入数据库失败");
            return (merged, errors, ex);
        }
    }

    private sealed class RefreshBindModel
    {
        public IReadOnlyList<OperatorStats> OpChartStats { get; set; } = Array.Empty<OperatorStats>();
        public IReadOnlyList<MonthStats> MonthStats { get; set; } = Array.Empty<MonthStats>();
        public string AvgLabel { get; set; } = "平均有效工时：—";
        public IReadOnlyList<OperatorStats> OpGridRows { get; set; } = Array.Empty<OperatorStats>();
    }

    private static RefreshBindModel ComputeRefreshData(
        HistoryRepository repo,
        HashSet<string> chartMonths,
        string? tableMonth,
        bool belowAvg)
    {
        var chartRecords = chartMonths.Count > 0
            ? repo.LoadRecordsForMonths(chartMonths)
            : new List<TestRecord>();
        var opStatsChart = PerformanceAggregator.AggregateByOperator(chartRecords).ToList();
        var monthStats = PerformanceAggregator.AggregateByMonth(chartRecords).ToList();

        if (string.IsNullOrEmpty(tableMonth))
        {
            return new RefreshBindModel
            {
                OpChartStats = opStatsChart,
                MonthStats = monthStats,
                AvgLabel = "平均有效工时：—",
                OpGridRows = Array.Empty<OperatorStats>()
            };
        }

        var tableRecords = repo.LoadRecordsForMonths(new HashSet<string> { tableMonth! });
        double avg = PerformanceAggregator.AverageEffectiveMinutesPerOperator(tableRecords);
        string avgLabel = $"平均有效工时：{PerformanceAggregator.FormatMinutes(avg)} 分（总有效÷人数）";
        var fullStats = PerformanceAggregator.AggregateByOperator(tableRecords).ToList();
        var display = belowAvg
            ? fullStats.Where(o => o.EffectiveMinutes < avg - 1e-9).ToList()
            : fullStats;

        return new RefreshBindModel
        {
            OpChartStats = opStatsChart,
            MonthStats = monthStats,
            AvgLabel = avgLabel,
            OpGridRows = display
        };
    }

    private void ApplyRefreshBind(RefreshBindModel m)
    {
        OperatorOeeChart.Bind(m.OpChartStats, Theme);
        BindMonthGrid(m.MonthStats);
        BindMonthChart(m.MonthStats);
        LblAvgEffective.Text = m.AvgLabel;
        BindOperatorGrid(m.OpGridRows);
    }

    private async Task LoadExcelFilesAsync(string[] paths)
    {
        if (paths == null || paths.Length == 0) return;

        var pathsCopy = paths.ToArray();
        Mouse.OverrideCursor = Cursors.Wait;
        SetImportUiBusy(true);

        ProgressImport.IsIndeterminate = false;
        ProgressImport.Minimum = 0;
        ProgressImport.Maximum = 100;
        ProgressImport.Value = 0;
        LblImportDetail.Text = "正在准备…";

        if (_panelTopIdleHeight <= 0)
            _panelTopIdleHeight = PanelTop.ActualHeight > 0 ? PanelTop.ActualHeight : 96;
        PanelImportProgress.Visibility = Visibility.Visible;

        IProgress<(int Percent, string Message)> phaseProgress = new Progress<(int Percent, string Message)>(v =>
        {
            Dispatcher.Invoke(() =>
            {
                int p = v.Percent;
                if (p < (int)ProgressImport.Minimum) p = (int)ProgressImport.Minimum;
                if (p > (int)ProgressImport.Maximum) p = (int)ProgressImport.Maximum;
                ProgressImport.Value = p;
                LblImportDetail.Text = v.Message;
            });
        });

        LblPath.Text = "【读文件 / 写库】处理中…";

        await _dataWorkLock.WaitAsync().ConfigureAwait(true);
        try
        {
            var repo = _repo;
            var (merged, errors, insertEx) = await Task.Run(() => ImportExcelFilesCore(pathsCopy, repo, phaseProgress)).ConfigureAwait(true);

            if (merged.Count == 0)
            {
                LblPath.Text = errors.Count > 0 ? "未能读取有效数据" : "未选择有效文件";
                if (errors.Count > 0)
                    MessageBox.Show(this, string.Join(Environment.NewLine, errors), "读取失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (insertEx != null)
            {
                MessageBox.Show(this, "写入本地数据库失败：" + insertEx.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LblPath.Text = pathsCopy.Length == 1
                ? $"{pathsCopy[0]}  ·  已写入本地库 {merged.Count} 条"
                : $"已合并 {pathsCopy.Length} 个文件，写入本地库 {merged.Count} 条";

            if (errors.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, errors), "部分文件读取失败", MessageBoxButton.OK, MessageBoxImage.Warning);

            RepopulateMonthUis(selectLatestTableMonth: true);

            phaseProgress.Report((100, "【刷新视图】正在汇总统计与图表…"));

            var chartMonths = new HashSet<string>(GetSelectedChartMonths(), StringComparer.Ordinal);
            string? tableMonth = CboPerfMonth.SelectedItem as string;
            bool belowAvg = ChkBelowAvg.IsChecked == true;
            var model = await Task.Run(() => ComputeRefreshData(repo, chartMonths, tableMonth, belowAvg)).ConfigureAwait(true);
            ApplyRefreshBind(model);
        }
        finally
        {
            PanelImportProgress.Visibility = Visibility.Collapsed;
            ProgressImport.Value = 0;
            LblImportDetail.Text = "";
            _dataWorkLock.Release();
            Mouse.OverrideCursor = null;
            SetImportUiBusy(false);
        }
    }

    private void RefreshViews() => _ = RefreshViewsCoreAsync();

    private async Task RefreshViewsCoreAsync()
    {
        await _dataWorkLock.WaitAsync().ConfigureAwait(true);
        try
        {
            var chartMonths = new HashSet<string>(GetSelectedChartMonths(), StringComparer.Ordinal);
            string? tableMonth = CboPerfMonth.SelectedItem as string;
            bool belowAvg = ChkBelowAvg.IsChecked == true;
            var repo = _repo;

            var model = await Task.Run(() => ComputeRefreshData(repo, chartMonths, tableMonth, belowAvg)).ConfigureAwait(true);
            ApplyRefreshBind(model);
        }
        finally
        {
            _dataWorkLock.Release();
        }
    }

    private void BindOperatorGrid(IReadOnlyList<OperatorStats> rows)
    {
        _operatorRows.Clear();
        foreach (var r in rows)
        {
            _operatorRows.Add(new OperatorGridRow
            {
                OperatorName = r.OperatorName,
                EffectiveMinutes = PerformanceAggregator.FormatMinutes(r.EffectiveMinutes),
                ActualMinutes = PerformanceAggregator.FormatMinutes(r.ActualMinutes),
                Oee = PerformanceAggregator.FormatOee(r.Oee),
                Contribution = PerformanceAggregator.FormatPercent(r.ContributionRatio)
            });
        }
    }

    private void BindMonthGrid(IReadOnlyList<MonthStats> rows)
    {
        _monthRows.Clear();
        foreach (var r in rows)
        {
            _monthRows.Add(new MonthGridRow
            {
                MonthKey = r.MonthKey,
                TotalEffective = PerformanceAggregator.FormatMinutes(r.TotalEffectiveMinutes),
                TotalActual = PerformanceAggregator.FormatMinutes(r.TotalActualMinutes),
                Oee = PerformanceAggregator.FormatOee(r.Oee)
            });
        }
    }

    private static OxyColor ToOxy(Color c) => OxyColor.FromArgb(c.A, c.R, c.G, c.B);

    private void BindMonthChart(IReadOnlyList<MonthStats> stats)
    {
        // OxyPlot 2.x 的 BarSeries 要求 CategoryAxis 在 Y 轴（横向柱），与「X=月份、Y=工时」不符。
        // 此处用底部 LinearAxis 表示月份序号 + LinearBarSeries 竖柱 + 右侧 Y 轴表示 OEE（总有效÷总实际）。
        var t = Theme;
        const string xKey = "MonthIndex";
        const string yMinutesKey = "Minutes";
        const string yOeeKey = "Oee";

        var monthKeys = stats.Select(s => s.MonthKey).ToList();
        int n = stats.Count;
        if (n == 0)
            monthKeys.Add("(无)");

        var model = new PlotModel
        {
            Background = ToOxy(t.ChartSurface),
            PlotAreaBackground = ToOxy(t.ChartSurface),
            PlotAreaBorderColor = ToOxy(t.ChartBorder),
            PlotAreaBorderThickness = new OxyThickness(1),
            Title = "按月：总有效工时、总实际工时（柱）与 OEE（折线，总有效÷总实际）",
            TitleColor = ToOxy(t.ChartTitleFg),
            TitleFont = "Microsoft YaHei UI",
            TitleFontSize = 14,
            TitlePadding = 4
        };

        model.Legends.Add(new Legend
        {
            LegendPlacement = LegendPlacement.Outside,
            LegendPosition = LegendPosition.BottomCenter,
            LegendOrientation = LegendOrientation.Horizontal,
            LegendMargin = 8,
            TextColor = ToOxy(t.ChartLegendFg)
        });

        int slotCount = Math.Max(1, n);
        var xAxis = new LinearAxis
        {
            Key = xKey,
            Position = AxisPosition.Bottom,
            Title = "月份",
            Minimum = -0.5,
            Maximum = slotCount - 0.5,
            MajorStep = 1,
            MinorStep = 1,
            AxislineColor = ToOxy(t.ChartAxisLine),
            TextColor = ToOxy(t.ChartLegendFg),
            TitleColor = ToOxy(t.ChartTitleFg),
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            LabelFormatter = v =>
            {
                int i = (int)Math.Round(v);
                if (Math.Abs(v - i) > 0.01) return string.Empty;
                if (i >= 0 && i < monthKeys.Count) return monthKeys[i];
                return string.Empty;
            }
        };

        var yMinutes = new LinearAxis
        {
            Key = yMinutesKey,
            Position = AxisPosition.Left,
            Title = "工时（分钟）",
            Minimum = 0,
            AxislineColor = ToOxy(t.ChartAxisLine),
            TextColor = ToOxy(t.ChartLegendFg),
            TitleColor = ToOxy(t.ChartTitleFg),
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = ToOxy(t.ChartGrid),
            MinorGridlineStyle = LineStyle.None
        };

        var yOee = new LinearAxis
        {
            Key = yOeeKey,
            Position = AxisPosition.Right,
            Title = "OEE（总有效÷总实际）",
            Minimum = 0,
            AxislineColor = ToOxy(Color.FromArgb(200, t.ChartLineOee.R, t.ChartLineOee.G, t.ChartLineOee.B)),
            TextColor = ToOxy(t.ChartLineOee),
            TitleColor = ToOxy(t.ChartLineOee),
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            LabelFormatter = v => v.ToString("P0", System.Globalization.CultureInfo.CurrentCulture)
        };

        model.Axes.Add(xAxis);
        model.Axes.Add(yMinutes);
        model.Axes.Add(yOee);

        const double barDx = 0.22;
        const double barW = 0.38;

        var sEff = new LinearBarSeries
        {
            Title = "总有效工时（该月全员之和）",
            XAxisKey = xKey,
            YAxisKey = yMinutesKey,
            FillColor = ToOxy(t.ChartColumnA),
            StrokeColor = ToOxy(Color.FromArgb(180, t.ChartColumnA.R, t.ChartColumnA.G, t.ChartColumnA.B)),
            StrokeThickness = 1,
            BarWidth = barW
        };

        var sAct = new LinearBarSeries
        {
            Title = "总实际工时（该月全员之和）",
            XAxisKey = xKey,
            YAxisKey = yMinutesKey,
            FillColor = ToOxy(t.ChartColumnB),
            StrokeColor = ToOxy(Color.FromArgb(180, t.ChartColumnB.R, t.ChartColumnB.G, t.ChartColumnB.B)),
            StrokeThickness = 1,
            BarWidth = barW
        };

        var sOee = new LineSeries
        {
            Title = "OEE",
            XAxisKey = xKey,
            YAxisKey = yOeeKey,
            Color = ToOxy(t.ChartLineOee),
            StrokeThickness = 3,
            MarkerType = MarkerType.Circle,
            MarkerSize = 5,
            MarkerFill = ToOxy(t.ChartSurface),
            MarkerStroke = ToOxy(t.ChartLineOee),
            MarkerStrokeThickness = 2
        };

        double maxMinutes = 0;
        double maxOee = 0;

        if (stats.Count == 0)
        {
            sEff.Points.Add(new DataPoint(-barDx, 0));
            sAct.Points.Add(new DataPoint(barDx, 0));
            sOee.Points.Add(new DataPoint(0, 0));
        }
        else
        {
            for (int i = 0; i < stats.Count; i++)
            {
                var m = stats[i];
                sEff.Points.Add(new DataPoint(i - barDx, m.TotalEffectiveMinutes));
                sAct.Points.Add(new DataPoint(i + barDx, m.TotalActualMinutes));
                maxMinutes = Math.Max(maxMinutes, m.TotalEffectiveMinutes);
                maxMinutes = Math.Max(maxMinutes, m.TotalActualMinutes);
                if (m.Oee.HasValue)
                {
                    sOee.Points.Add(new DataPoint(i, m.Oee.Value));
                    maxOee = Math.Max(maxOee, m.Oee.Value);
                }
                else
                    sOee.Points.Add(DataPoint.Undefined);
            }
        }

        yMinutes.Maximum = maxMinutes <= 0 ? double.NaN : maxMinutes * 1.15;
        yOee.Maximum = maxOee <= 0 ? 1 : Math.Max(maxOee * 1.12, 1.02);

        model.Series.Add(sEff);
        model.Series.Add(sAct);
        model.Series.Add(sOee);

        PlotMonth.Model = model;
    }

    private static void ApplyPlotTheme(PlotModel model, UiTheme t)
    {
        model.Background = ToOxy(t.ChartSurface);
        model.PlotAreaBackground = ToOxy(t.ChartSurface);
        model.PlotAreaBorderColor = ToOxy(t.ChartBorder);
        model.TitleColor = ToOxy(t.ChartTitleFg);
        foreach (var axis in model.Axes)
        {
            axis.TextColor = axis is LinearAxis la && la.Key == "Oee" ? ToOxy(t.ChartLineOee) : ToOxy(t.ChartLegendFg);
            axis.TitleColor = axis is LinearAxis la2 && la2.Key == "Oee" ? ToOxy(t.ChartLineOee) : ToOxy(t.ChartTitleFg);
            axis.AxislineColor = ToOxy(t.ChartAxisLine);
            if (axis is LinearAxis l && l.MajorGridlineStyle != LineStyle.None)
                l.MajorGridlineColor = ToOxy(t.ChartGrid);
        }
        foreach (var leg in model.Legends)
            leg.TextColor = ToOxy(t.ChartLegendFg);
    }

    private void BtnExportOperatorChart_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp",
            FileName = "操作员OEE_" + DateTime.Now.ToString("yyyyMMdd_HHmm"),
            DefaultExt = "png",
            AddExtension = true
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            ExportFrameworkElementToImage(OperatorOeeChart, dlg.FileName);
            MessageBox.Show(this, $"已保存：{dlg.FileName}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnExportMonthChart_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp",
            FileName = "月度工时与OEE_" + DateTime.Now.ToString("yyyyMMdd_HHmm"),
            DefaultExt = "png",
            AddExtension = true
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            ExportFrameworkElementToImage(PlotMonth, dlg.FileName);
            MessageBox.Show(this, $"已保存：{dlg.FileName}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void ExportFrameworkElementToImage(FrameworkElement el, string filePath)
    {
        el.UpdateLayout();
        double w = el.ActualWidth;
        double h = el.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            w = 960;
            h = 420;
            el.Measure(new Size(w, h));
            el.Arrange(new Rect(0, 0, w, h));
            el.UpdateLayout();
        }

        int iw = Math.Max(1, (int)Math.Ceiling(el.ActualWidth));
        int ih = Math.Max(1, (int)Math.Ceiling(el.ActualHeight));
        var rtb = new RenderTargetBitmap(iw, ih, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(el);

        var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
        BitmapEncoder enc = ext switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 90 },
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(filePath);
        enc.Save(fs);
    }
}
