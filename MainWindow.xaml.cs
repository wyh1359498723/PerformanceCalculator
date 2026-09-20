using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace PerformanceCalculator2;

public partial class MainWindow : Window
{
    private readonly HistoryRepository _repo = new();
    private readonly SemaphoreSlim _dataWorkLock = new(1, 1);
    private string? _operatorTrendName;
    private bool _suppressOperatorSelectionRefresh;
    private readonly ObservableCollection<string> _perfMonthItems = new();
    private readonly ObservableCollection<SelectableMonth> _chartMonthItems = new();
    private readonly ObservableCollection<OperatorGridRow> _operatorRows = new();
    private readonly ObservableCollection<MonthGridRow> _monthRows = new();

    private bool _suppressMonthUi;
    private bool _initializingUi = true;
    private const int ImportProgressStripHeight = 42;
    private double _panelTopIdleHeight;
    private Storyboard? _dragPulseStoryboard;

    /// <summary>默认浅色；勾选「科幻深色」为 Tech 主题。</summary>
    private UiTheme Theme => ChkDarkMode.IsChecked == true ? UiTheme.Tech : UiTheme.Light;

    private bool IsTechChrome => ChkDarkMode.IsChecked == true;

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
        DgvOperators.SelectionChanged += DgvOperators_SelectionChanged;
        Loaded += MainWindow_Loaded;
        _initializingUi = false;
    }

    private void PanelTop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标在拖动开始前已释放时无需处理。
        }
    }

    private void BtnWindowMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnWindowMaximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void BtnWindowClose_Click(object sender, RoutedEventArgs e) => Close();

    private void DgvOperators_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressOperatorSelectionRefresh) return;
        string? next = DgvOperators.SelectedItem is OperatorGridRow r ? r.OperatorName : null;
        if (string.Equals(next, _operatorTrendName, StringComparison.OrdinalIgnoreCase))
            return;
        _operatorTrendName = next;
        RefreshViews();
    }

    private void RestoreOperatorGridSelection()
    {
        if (string.IsNullOrEmpty(_operatorTrendName)) return;
        var match = _operatorRows.FirstOrDefault(r =>
            string.Equals(r.OperatorName, _operatorTrendName, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            _operatorTrendName = null;
            return;
        }

        _suppressOperatorSelectionRefresh = true;
        try
        {
            DgvOperators.SelectedItem = match;
        }
        finally
        {
            _suppressOperatorSelectionRefresh = false;
        }
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
        DgvOperators.CanUserSortColumns = true;
        DgvOperators.Columns.Add(CreateGridColumn("操作员  ↕", nameof(OperatorGridRow.OperatorName), nameof(OperatorGridRow.OperatorName), new DataGridLength(1, DataGridLengthUnitType.Star), false));
        DgvOperators.Columns.Add(CreateGridColumn("有效工时(分)  ↕", nameof(OperatorGridRow.EffectiveMinutes), nameof(OperatorGridRow.EffectiveMinutesValue), new DataGridLength(1, DataGridLengthUnitType.Star), true));
        DgvOperators.Columns.Add(CreateGridColumn("实际工时(分)  ↕", nameof(OperatorGridRow.ActualMinutes), nameof(OperatorGridRow.ActualMinutesValue), new DataGridLength(1, DataGridLengthUnitType.Star), true));
        DgvOperators.Columns.Add(CreateGridColumn("OEE  ↕", nameof(OperatorGridRow.Oee), nameof(OperatorGridRow.OeeValue), new DataGridLength(88), true));
        DgvOperators.Columns.Add(CreateGridColumn("产出贡献占比  ↕", nameof(OperatorGridRow.Contribution), nameof(OperatorGridRow.ContributionValue), new DataGridLength(1, DataGridLengthUnitType.Star), true));

        DgvMonths.Columns.Clear();
        DgvMonths.CanUserSortColumns = true;
        DgvMonths.Columns.Add(CreateGridColumn("月份  ↕", nameof(MonthGridRow.MonthKey), nameof(MonthGridRow.MonthKey), new DataGridLength(100), false));
        DgvMonths.Columns.Add(CreateGridColumn("总有效工时(分)  ↕", nameof(MonthGridRow.TotalEffective), nameof(MonthGridRow.TotalEffectiveValue), new DataGridLength(1, DataGridLengthUnitType.Star), true));
        DgvMonths.Columns.Add(CreateGridColumn("总实际工时(分)  ↕", nameof(MonthGridRow.TotalActual), nameof(MonthGridRow.TotalActualValue), new DataGridLength(1, DataGridLengthUnitType.Star), true));
        DgvMonths.Columns.Add(CreateGridColumn("OEE  ↕", nameof(MonthGridRow.Oee), nameof(MonthGridRow.OeeValue), new DataGridLength(100), true));
    }

    private static DataGridTextColumn CreateGridColumn(
        string header,
        string displayProperty,
        string sortProperty,
        DataGridLength width,
        bool center)
    {
        var textStyle = new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center));
        textStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,
            center ? System.Windows.TextAlignment.Center : System.Windows.TextAlignment.Left));

        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(displayProperty),
            SortMemberPath = sortProperty,
            Width = width,
            ElementStyle = textStyle
        };
    }

    private void RepopulateMonthUis(
        bool selectLatestTableMonth,
        string? preferredTableMonth = null,
        ISet<string>? preferredChartMonths = null)
    {
        var months = _repo.ListDistinctMonths().OrderByDescending(m => m, StringComparer.Ordinal).ToList();
        string? tableMonthToRestore = preferredTableMonth;
        HashSet<string>? chartMonthsToRestore = preferredChartMonths == null
            ? null
            : new HashSet<string>(preferredChartMonths, StringComparer.Ordinal);

        _suppressMonthUi = true;
        try
        {
            _perfMonthItems.Clear();
            foreach (var m in months)
                _perfMonthItems.Add(m);

            _chartMonthItems.Clear();
            foreach (var m in months.OrderBy(x => x, StringComparer.Ordinal))
            {
                _chartMonthItems.Add(new SelectableMonth(m)
                {
                    IsSelected = chartMonthsToRestore == null || chartMonthsToRestore.Contains(m)
                });
            }

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
                if (!string.IsNullOrEmpty(tableMonthToRestore) && months.Contains(tableMonthToRestore!))
                    CboPerfMonth.SelectedItem = tableMonthToRestore;
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
        string? tableMonth = CboPerfMonth.SelectedItem as string;
        var chartMonths = GetSelectedChartMonths();
        var w = new HistoryWindow(_repo) { Owner = this };
        w.ShowDialog();
        if (!w.HasChanges)
            return;

        RepopulateMonthUis(
            selectLatestTableMonth: false,
            preferredTableMonth: tableMonth,
            preferredChartMonths: chartMonths);
        RefreshViews();
    }

    private void ChkDarkMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingUi) return;
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
        bool tech = IsTechChrome;

        RootChrome.Background = tech ? new SolidColorBrush(Color.FromRgb(7, 17, 29)) : UiTheme.Solid(t.Bg);
        Background = Brushes.Transparent;
        Foreground = UiTheme.Solid(t.TextPrimary);
        MainGrid.Background = Brushes.Transparent;
        TechBackdrop.Visibility = tech ? Visibility.Visible : Visibility.Collapsed;

        if (tech)
        {
            var hdr = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 0.2)
            };
            hdr.GradientStops.Add(new GradientStop(Color.FromRgb(10, 32, 51), 0));
            hdr.GradientStops.Add(new GradientStop(Color.FromRgb(8, 22, 37), 1));
            PanelTop.Background = hdr;
            LblPath.Foreground = new SolidColorBrush(Color.FromRgb(148, 180, 200));
        }
        else
        {
            PanelTop.Background = UiTheme.Solid(t.Header);
            LblPath.Foreground = UiTheme.Solid(t.TextMuted);
        }

        LblTitle.Foreground = UiTheme.Solid(Color.FromRgb(240, 249, 255));
        BadgeLine.Background = UiTheme.Solid(tech ? Color.FromRgb(18, 31, 49) : t.GridHeader);
        BadgeLine.BorderBrush = UiTheme.Solid(tech ? Color.FromArgb(0x66, 34, 211, 238) : t.GridLine);
        LblBadge.Foreground = UiTheme.Solid(tech ? Color.FromRgb(125, 211, 252) : t.TextMuted);

        ApplyChromeButtons();
        ApplyDropZoneIdle();
        LblDropHint.Foreground = tech ? new SolidColorBrush(Color.FromRgb(125, 211, 252)) : UiTheme.Solid(t.TextMuted);

        if (tech)
        {
            PanelImportProgress.Background = new SolidColorBrush(Color.FromRgb(12, 15, 28));
            PanelImportProgress.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 34, 211, 238));
            LblImportDetail.Foreground = new SolidColorBrush(Color.FromRgb(186, 230, 253));
            if (TryFindResource("TechProgressBar") is Style pbs)
                ProgressImport.Style = pbs;
        }
        else
        {
            PanelImportProgress.Background = UiTheme.Solid(t.Header);
            PanelImportProgress.BorderBrush = UiTheme.Solid(t.GridLine);
            LblImportDetail.Foreground = Brushes.White;
            ProgressImport.Style = null;
        }

        CboPerfMonth.Background = UiTheme.Solid(t.GridBg);
        CboPerfMonth.Foreground = UiTheme.Solid(t.TextPrimary);
        LblAvgEffective.Foreground = UiTheme.Solid(t.TextPrimary);
        LblPerfMonthCaption.Foreground = UiTheme.Solid(t.TextMuted);
        ChkBelowAvg.Foreground = UiTheme.Solid(t.TextPrimary);

        if (tech && TryFindResource("TechComboBox") is Style cbSt)
            CboPerfMonth.Style = cbSt;
        else
            CboPerfMonth.Style = null;

        if (tech && TryFindResource("TechCheckBox") is Style chkSt)
        {
            ChkBelowAvg.Style = chkSt;
        }
        else
        {
            ChkBelowAvg.Style = null;
            ChkBelowAvg.Foreground = UiTheme.Solid(t.TextPrimary);
        }
        ChkDarkMode.Style = (Style)FindResource("DashboardThemeToggle");

        ApplyMonthPickerChrome(tech, t);
        ApplyChromeCards(tech, t);
        PlotMonth.Background = UiTheme.Solid(t.ChartSurface);
        PlotMonth.Foreground = UiTheme.Solid(t.ChartLegendFg);
        Resources["PlotTrackerBackgroundBrush"] = UiTheme.Solid(tech
            ? Color.FromArgb(242, 11, 27, 43)
            : Color.FromArgb(248, 255, 255, 255));
        Resources["PlotTrackerBorderBrush"] = UiTheme.Solid(tech
            ? Color.FromRgb(70, 130, 165)
            : Color.FromRgb(148, 163, 184));
        Resources["PlotTrackerTextBrush"] = UiTheme.Solid(tech
            ? Color.FromRgb(232, 247, 255)
            : Color.FromRgb(30, 41, 59));
    }

    /// <summary>图表统计月份多选区：背景与文字强对比（浅色主题下避免深色底+深色字）。</summary>
    private void ApplyMonthPickerChrome(bool tech, UiTheme t)
    {
        if (tech)
        {
            BdChartMonthPicker.Background = new SolidColorBrush(Color.FromRgb(10, 20, 38));
            BdChartMonthPicker.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 34, 211, 238));
            ChartMonthItemsControl.Foreground = new SolidColorBrush(Color.FromRgb(248, 252, 255));
            LblChartMonthHint.Foreground = new SolidColorBrush(Color.FromRgb(165, 220, 245));
        }
        else
        {
            BdChartMonthPicker.Background = UiTheme.Solid(Colors.White);
            BdChartMonthPicker.BorderBrush = UiTheme.Solid(t.GridLine);
            ChartMonthItemsControl.Foreground = UiTheme.Solid(t.TextPrimary);
            LblChartMonthHint.Foreground = UiTheme.Solid(t.TextMuted);
        }
    }

    private void ApplyChromeButtons()
    {
        var headerStyle = (Style)FindResource("DashboardHeaderButton");
        BtnExportOperatorChart.Style = headerStyle;
        BtnExportMonthChart.Style = headerStyle;
        BtnHistory.Style = headerStyle;
        BtnSelectFiles.Style = (Style)FindResource("DashboardPrimaryButton");
        BtnExportPerfMonth.Style = (Style)FindResource("DashboardFilterButton");
        TglChartMonths.Style = (Style)FindResource("DashboardFilterToggle");
    }

    private void ApplyChromeCards(bool tech, UiTheme t)
    {
        CardLeft.Background = Brushes.Transparent;
        CardRight.Background = Brushes.Transparent;

        var cards = new[]
        {
            PanelPerf, CardOperatorTable, CardMonthTable,
            CardOperatorChart, CardMonthChart,
            KpiCardOperators, KpiCardEffective, KpiCardActual, KpiCardOee
        };

        if (tech)
        {
            foreach (var card in cards)
            {
                card.Style = null;
                card.Background = UiTheme.Solid(Color.FromRgb(11, 27, 43));
                card.BorderBrush = UiTheme.Solid(Color.FromRgb(39, 70, 94));
                card.BorderThickness = new Thickness(1);
                card.CornerRadius = new CornerRadius(5);
                card.Effect = null;
            }

            var kpiBrush = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1)
            };
            kpiBrush.GradientStops.Add(new GradientStop(Color.FromRgb(17, 39, 59), 0));
            kpiBrush.GradientStops.Add(new GradientStop(Color.FromRgb(12, 30, 47), 1));
            foreach (var card in new[] { KpiCardOperators, KpiCardEffective, KpiCardActual, KpiCardOee })
                card.Background = kpiBrush;

            PanelPerf.Background = UiTheme.Solid(Color.FromRgb(13, 34, 53));
        }
        else
        {
            foreach (var card in cards)
            {
                card.Style = null;
                card.Background = UiTheme.Solid(t.GridBg);
                card.BorderBrush = UiTheme.Solid(t.GridLine);
                card.BorderThickness = new Thickness(1);
                card.CornerRadius = new CornerRadius(5);
                card.Effect = null;
            }
        }

        CardOperatorChart.Background = UiTheme.Solid(t.ChartSurface);
        CardMonthChart.Background = UiTheme.Solid(t.ChartSurface);

        foreach (var caption in new[] { KpiCaptionOperators, KpiCaptionEffective, KpiCaptionActual, KpiCaptionOee })
            caption.Foreground = UiTheme.Solid(t.TextMuted);
        foreach (var value in new[] { LblKpiOperatorCount, LblKpiEffective, LblKpiActual })
            value.Foreground = UiTheme.Solid(t.TextPrimary);
        LblKpiOee.Foreground = UiTheme.Solid(t.Accent);
        LblOperatorTableTitle.Foreground = UiTheme.Solid(t.TextPrimary);
        LblMonthTableTitle.Foreground = UiTheme.Solid(t.TextPrimary);
    }

    private void ApplyDropZoneIdle()
    {
        bool tech = IsTechChrome;
        var t = Theme;
        if (tech && TryFindResource("DropZoneTechBrush") is Brush db)
            PanelDrop.Background = db;
        else
            PanelDrop.Background = UiTheme.Solid(t.DropZone);
        PanelDrop.BorderBrush = tech
            ? new SolidColorBrush(Color.FromArgb(0x77, 34, 211, 238))
            : UiTheme.Solid(t.GridLine);
        DropScanLine.Visibility = Visibility.Collapsed;
        _dragPulseStoryboard?.Stop(DropScanLine);
    }

    private void StyleDataGrid(DataGrid dg)
    {
        var t = Theme;
        dg.Background = UiTheme.Solid(t.GridBg);
        dg.BorderThickness = new Thickness(0);
        dg.HorizontalGridLinesBrush = UiTheme.Solid(t.GridLine);
        dg.RowBackground = UiTheme.Solid(t.GridBg);
        dg.AlternatingRowBackground = UiTheme.Solid(t.GridAlt);
        dg.AlternationCount = 2;
        dg.Foreground = UiTheme.Solid(t.TextPrimary);
        dg.VerticalGridLinesBrush = IsTechChrome ? UiTheme.Solid(t.GridLine) : Brushes.Transparent;
        dg.GridLinesVisibility = IsTechChrome ? DataGridGridLinesVisibility.All : DataGridGridLinesVisibility.Horizontal;
        dg.RowHeight = 31;
        dg.ColumnHeaderHeight = 34;
        dg.CanUserResizeRows = false;

        var headerStyle = new Style(typeof(DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(BackgroundProperty, UiTheme.Solid(t.GridHeader)));
        headerStyle.Setters.Add(new Setter(ForegroundProperty, UiTheme.Solid(t.TextPrimary)));
        headerStyle.Setters.Add(new Setter(FontWeightProperty, System.Windows.FontWeights.Bold));
        headerStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(8, 6, 8, 6)));
        headerStyle.Setters.Add(new Setter(CursorProperty, Cursors.Hand));
        headerStyle.Setters.Add(new Setter(ToolTipProperty, "点击按此列排序"));
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
            if (IsTechChrome)
            {
                PanelDrop.BorderBrush = new SolidColorBrush(Color.FromRgb(125, 230, 255));
                PanelDrop.Background = new SolidColorBrush(Color.FromArgb(0xD0, 16, 28, 48));
                DropScanLine.Visibility = Visibility.Visible;
                _dragPulseStoryboard ??= TryFindResource("DragPulseStoryboard") as Storyboard;
                _dragPulseStoryboard?.Begin(DropScanLine, true);
            }
            else
                PanelDrop.Background = UiTheme.Solid(Theme.DropZoneHover);
        }
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void PanelDrop_DragLeave(object sender, DragEventArgs e) => ApplyDropZoneIdle();

    private void PanelDrop_Drop(object sender, DragEventArgs e)
    {
        ApplyDropZoneIdle();
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
        BtnExportOperatorChart.IsEnabled = !busy;
        BtnExportMonthChart.IsEnabled = !busy;
        BtnExportPerfMonth.IsEnabled = !busy;
        ChkDarkMode.IsEnabled = !busy;
        PanelDrop.IsHitTestVisible = !busy;
        ChartMonthItemsControl.IsEnabled = !busy;
        CboPerfMonth.IsEnabled = !busy;
        ChkBelowAvg.IsEnabled = !busy;
        BtnHistory.IsEnabled = !busy;
    }

    private static (List<TestRecord> importedRows, int importedSources, List<string> errors, Exception? insertEx) ImportExcelFilesCore(
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

        var importedRows = new List<TestRecord>();
        var sources = new List<(string Path, IReadOnlyList<TestRecord> Rows)>();
        var errors = new List<string>();
        int nFiles = pathsCopy.Length;
        if (nFiles == 0)
        {
            Report(0, "未选择文件");
            return (importedRows, 0, errors, null);
        }

        for (int fi = 0; fi < nFiles; fi++)
        {
            string path = pathsCopy[fi];
            int pctRead = (int)(40.0 * fi / nFiles);
            Report(pctRead, $"【读文件】({fi + 1}/{nFiles}) {Path.GetFileName(path)}");
            try
            {
                if (!File.Exists(path))
                {
                    errors.Add($"{Path.GetFileName(path)}：文件不存在");
                    continue;
                }
                var part = ExcelPerformanceReader.ReadWorkbook(path);
                if (part.Count == 0)
                {
                    errors.Add($"{Path.GetFileName(path)}：未读取到有效的 RP0 数据");
                    continue;
                }
                sources.Add((path, part));
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}：读取失败：{ex.Message}");
            }
        }

        if (sources.Count == 0)
        {
            Report(0, errors.Count > 0 ? "未能读取到有效数据" : "未选择有效文件");
            return (importedRows, 0, errors, null);
        }

        int totalRows = sources.Sum(s => s.Rows.Count);
        int processedRows = 0;
        int importedSources = 0;
        Exception? firstInsertException = null;

        for (int si = 0; si < sources.Count; si++)
        {
            var source = sources[si];
            string sourceName = Path.GetFileName(source.Path);
            int sourceNumber = si + 1;
            int sourceBase = processedRows;
            IProgress<(int current, int total)>? rowProgress = null;
            if (phaseProgress != null)
            {
                rowProgress = new Progress<(int current, int total)>(t =>
                {
                    int currentTotal = sourceBase + t.current;
                    int p = totalRows <= 0 ? 40 : (int)(40 + 59.0 * currentTotal / totalRows);
                    if (p > 99) p = 99;
                    phaseProgress.Report((p,
                        $"【写入数据源】({sourceNumber}/{sources.Count}) {sourceName} · {currentTotal:N0}/{totalRows:N0} 条"));
                });
            }

            try
            {
                repo.InsertDataSource(source.Rows, sourceName, rowProgress);
                importedRows.AddRange(source.Rows);
                importedSources++;
            }
            catch (Exception ex)
            {
                firstInsertException ??= ex;
                errors.Add($"{sourceName}：写入失败：{ex.Message}");
            }
            finally
            {
                processedRows += source.Rows.Count;
            }
        }

        if (importedSources > 0)
            Report(100, $"【写入数据源】完成，共 {importedSources} 个数据源、{importedRows.Count:N0} 条记录");
        else
            Report(0, "数据源写入失败");

        return (importedRows, importedSources, errors, importedSources == 0 ? firstInsertException : null);
    }

    private sealed class RefreshBindModel
    {
        public IReadOnlyList<OperatorStats> OpChartStats { get; set; } = Array.Empty<OperatorStats>();
        public IReadOnlyList<MonthStats> MonthStats { get; set; } = Array.Empty<MonthStats>();
        public string AvgLabel { get; set; } = "平均有效工时：—";
        public string OperatorCountLabel { get; set; } = "—";
        public string EffectiveTotalLabel { get; set; } = "—";
        public string ActualTotalLabel { get; set; } = "—";
        public string OeeLabel { get; set; } = "—";
        public IReadOnlyList<OperatorStats> OpGridRows { get; set; } = Array.Empty<OperatorStats>();
        public IReadOnlyList<double?>? OperatorTrendOee { get; set; }
        public string? OperatorTrendSeriesTitle { get; set; }
    }

    private static RefreshBindModel ComputeRefreshData(
        HistoryRepository repo,
        HashSet<string> chartMonths,
        string? tableMonth,
        bool belowAvg,
        string? operatorTrendForChart)
    {
        var chartRecords = chartMonths.Count > 0
            ? repo.LoadRecordsForMonths(chartMonths)
            : new List<TestRecord>();
        var opStatsChart = PerformanceAggregator.AggregateByOperator(chartRecords).ToList();
        var monthStats = PerformanceAggregator.AggregateByMonth(chartRecords).ToList();
        var trend = PerformanceAggregator.OperatorMonthlyOeeSeries(chartRecords, operatorTrendForChart, monthStats);
        string? trendTitle = trend == null || string.IsNullOrWhiteSpace(operatorTrendForChart)
            ? null
            : $"{operatorTrendForChart.Trim()} · 各月 OEE";

        if (string.IsNullOrEmpty(tableMonth))
        {
            return new RefreshBindModel
            {
                OpChartStats = opStatsChart,
                MonthStats = monthStats,
                AvgLabel = "平均有效工时：—",
                OpGridRows = Array.Empty<OperatorStats>(),
                OperatorTrendOee = trend,
                OperatorTrendSeriesTitle = trendTitle
            };
        }

        var tableRecords = repo.LoadRecordsForMonths(new HashSet<string> { tableMonth! });
        double avg = PerformanceAggregator.AverageEffectiveMinutesPerOperator(tableRecords);
        string avgLabel = $"平均有效工时：{PerformanceAggregator.FormatMinutes(avg)} 分（总有效÷人数）";
        var fullStats = PerformanceAggregator.AggregateByOperator(tableRecords).ToList();
        double totalEffective = tableRecords.Sum(r => r.TestTimeMinutes);
        double totalActual = tableRecords.Sum(r => r.ActualTimeMinutes);
        double? overallOee = totalActual > PerformanceMetrics.MinActualMinutesForOee
            ? totalEffective / totalActual
            : null;
        var display = belowAvg
            ? fullStats.Where(o => o.EffectiveMinutes < avg - 1e-9).ToList()
            : fullStats;

        return new RefreshBindModel
        {
            // 与绩效表格同月：仅含该月有记录的操作员，避免跨「图表统计月份」汇总时离职人员仍出现在后续月份视图
            OpChartStats = fullStats,
            MonthStats = monthStats,
            AvgLabel = avgLabel,
            OperatorCountLabel = fullStats.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            EffectiveTotalLabel = totalEffective.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            ActualTotalLabel = totalActual.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            OeeLabel = PerformanceAggregator.FormatOee(overallOee),
            OpGridRows = display,
            OperatorTrendOee = trend,
            OperatorTrendSeriesTitle = trendTitle
        };
    }

    /// <param name="operatorKeyForTrend">本次汇总时用于操作员 OEE 曲线的姓名（与 <see cref="ComputeRefreshData"/> 传入值一致，避免刷新表格时 SelectionChanged 清空字段导致不画线）。</param>
    private void ApplyRefreshBind(RefreshBindModel m, string? operatorKeyForTrend)
    {
        OperatorOeeChart.Bind(m.OpChartStats, Theme);
        BindMonthGrid(m.MonthStats);
        LblAvgEffective.Text = m.AvgLabel;
        LblKpiOperatorCount.Text = m.OperatorCountLabel;
        LblKpiEffective.Text = m.EffectiveTotalLabel;
        LblKpiActual.Text = m.ActualTotalLabel;
        LblKpiOee.Text = m.OeeLabel;
        BindOperatorGrid(m.OpGridRows);
        RestoreOperatorGridSelection();
        bool showTrend = !string.IsNullOrEmpty(operatorKeyForTrend)
            && m.OperatorTrendOee != null
            && !string.IsNullOrEmpty(m.OperatorTrendSeriesTitle);
        BindMonthChart(
            m.MonthStats,
            showTrend ? m.OperatorTrendOee : null,
            showTrend ? m.OperatorTrendSeriesTitle : null);
    }

    private async Task LoadExcelFilesAsync(string[] paths)
    {
        if (paths == null || paths.Length == 0) return;

        var pathsCopy = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
            var (importedRows, importedSources, errors, insertEx) = await Task.Run(() => ImportExcelFilesCore(pathsCopy, repo, phaseProgress)).ConfigureAwait(true);

            if (importedRows.Count == 0)
            {
                bool writeFailed = insertEx != null;
                LblPath.Text = writeFailed ? "数据源写入失败" : "未能读取有效数据";
                string detail = errors.Count > 0
                    ? string.Join(Environment.NewLine, errors)
                    : insertEx?.Message ?? "未选择有效文件";
                MessageBox.Show(this, detail, writeFailed ? "写入失败" : "读取失败", MessageBoxButton.OK,
                    writeFailed ? MessageBoxImage.Error : MessageBoxImage.Warning);
                return;
            }

            LblPath.Text = importedSources == 1 && pathsCopy.Length == 1
                ? $"{pathsCopy[0]}  ·  已作为独立数据源写入 {importedRows.Count} 条"
                : $"已导入 {importedSources} 个独立数据源，共写入 {importedRows.Count} 条";

            if (errors.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, errors), "部分数据源导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);

            RepopulateMonthUis(selectLatestTableMonth: true);

            phaseProgress.Report((100, "【刷新视图】正在汇总统计与图表…"));

            var chartMonths = new HashSet<string>(GetSelectedChartMonths(), StringComparer.Ordinal);
            string? tableMonth = CboPerfMonth.SelectedItem as string;
            bool belowAvg = ChkBelowAvg.IsChecked == true;
            string? opTrend = _operatorTrendName;
            var model = await Task.Run(() => ComputeRefreshData(repo, chartMonths, tableMonth, belowAvg, opTrend)).ConfigureAwait(true);
            ApplyRefreshBind(model, opTrend);
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

            string? opTrend = _operatorTrendName;
            var model = await Task.Run(() => ComputeRefreshData(repo, chartMonths, tableMonth, belowAvg, opTrend)).ConfigureAwait(true);
            ApplyRefreshBind(model, opTrend);
        }
        finally
        {
            _dataWorkLock.Release();
        }
    }

    private void BindOperatorGrid(IReadOnlyList<OperatorStats> rows)
    {
        _suppressOperatorSelectionRefresh = true;
        try
        {
            _operatorRows.Clear();
            foreach (var r in rows)
            {
                _operatorRows.Add(new OperatorGridRow
                {
                    OperatorName = r.OperatorName,
                    EffectiveMinutesValue = r.EffectiveMinutes,
                    ActualMinutesValue = r.ActualMinutes,
                    OeeValue = r.Oee,
                    ContributionValue = r.ContributionRatio,
                    EffectiveMinutes = PerformanceAggregator.FormatMinutes(r.EffectiveMinutes),
                    ActualMinutes = PerformanceAggregator.FormatMinutes(r.ActualMinutes),
                    Oee = PerformanceAggregator.FormatOee(r.Oee),
                    Contribution = PerformanceAggregator.FormatPercent(r.ContributionRatio)
                });
            }
        }
        finally
        {
            _suppressOperatorSelectionRefresh = false;
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
                TotalEffectiveValue = r.TotalEffectiveMinutes,
                TotalActualValue = r.TotalActualMinutes,
                OeeValue = r.Oee,
                TotalEffective = PerformanceAggregator.FormatMinutes(r.TotalEffectiveMinutes),
                TotalActual = PerformanceAggregator.FormatMinutes(r.TotalActualMinutes),
                Oee = PerformanceAggregator.FormatOee(r.Oee)
            });
        }
    }

    private static OxyColor ToOxy(Color c) => OxyColor.FromArgb(c.A, c.R, c.G, c.B);

    private static void ApplySmoothOeeLine(LineSeries series)
    {
        int valid = series.Points.Count(p => !double.IsNaN(p.Y));
        series.InterpolationAlgorithm = valid >= 3 ? InterpolationAlgorithms.CatmullRomSpline : null;
    }

    private static void AddOeeValueLabel(
        PlotModel model,
        int monthIndex,
        double value,
        OxyColor textColor,
        OxyColor surfaceColor,
        bool placeAbove)
    {
        model.Annotations.Add(new TextAnnotation
        {
            XAxisKey = "MonthIndex",
            YAxisKey = "Oee",
            Text = value.ToString("P1", System.Globalization.CultureInfo.CurrentCulture),
            TextPosition = new DataPoint(monthIndex, value),
            Offset = new ScreenVector(0, placeAbove ? -15 : 15),
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
            TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle,
            TextColor = textColor,
            Font = "Microsoft YaHei UI",
            FontSize = 10.5,
            FontWeight = 600,
            Background = OxyColor.FromArgb(225, surfaceColor.R, surfaceColor.G, surfaceColor.B),
            Stroke = OxyColor.FromArgb(105, textColor.R, textColor.G, textColor.B),
            StrokeThickness = 1,
            Padding = new OxyThickness(3, 1, 3, 1)
        });
    }

    private void BindMonthChart(
        IReadOnlyList<MonthStats> stats,
        IReadOnlyList<double?>? operatorTrend,
        string? operatorTrendTitle)
    {
        // 底部 LinearAxis 月份序号 + LinearBarSeries 竖柱 + 右侧 OEE；OEE 使用 Catmull-Rom 平滑。
        var t = Theme;
        const string xKey = "MonthIndex";
        const string yMinutesKey = "Minutes";
        const string yOeeKey = "Oee";

        var monthKeys = stats.Select(s => s.MonthKey).ToList();
        int n = stats.Count;
        if (n == 0)
            monthKeys.Add("(无)");

        bool showOperatorTrend = operatorTrend != null
            && stats.Count > 0
            && operatorTrend.Count == stats.Count
            && !string.IsNullOrWhiteSpace(operatorTrendTitle);
        string chartTitle = showOperatorTrend
            ? "月度工时与 OEE 趋势（全员 + 所选操作员）"
            : "月度工时与 OEE 趋势";

        var model = new PlotModel
        {
            Background = ToOxy(t.ChartSurface),
            PlotAreaBackground = ToOxy(t.ChartSurface),
            PlotAreaBorderColor = ToOxy(t.ChartBorder),
            PlotAreaBorderThickness = new OxyThickness(1),
            TextColor = ToOxy(t.ChartLegendFg),
            Title = chartTitle,
            TitleColor = ToOxy(t.ChartTitleFg),
            TitleFont = "Microsoft YaHei UI",
            TitleFontSize = 14,
            TitlePadding = 4
        };

        model.Legends.Add(new Legend
        {
            LegendPlacement = LegendPlacement.Outside,
            LegendPosition = LegendPosition.TopRight,
            LegendOrientation = LegendOrientation.Horizontal,
            LegendMargin = 5,
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
        // LinearBarSeries.BarWidth 使用屏幕像素，而不是 X 轴的数据单位。
        // 之前的 0.38 会被渲染成近似一条细线；根据月份数量适当收窄，兼顾稀疏和密集数据。
        double barW = n switch
        {
            <= 6 => 22,
            <= 12 => 16,
            <= 18 => 11,
            _ => 8
        };

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
            Title = "全员月度 OEE",
            TrackerFormatString = "{0}\n{3}：{4:P1}",
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
        double minOee = double.MaxValue;

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
                    minOee = Math.Min(minOee, m.Oee.Value);
                }
                else
                    sOee.Points.Add(DataPoint.Undefined);
            }
        }

        ApplySmoothOeeLine(sOee);

        LineSeries? sOpTrend = null;
        if (showOperatorTrend)
        {
            var opRgb = Color.FromRgb(232, 121, 249);
            sOpTrend = new LineSeries
            {
                Title = operatorTrendTitle!.Trim(),
                TrackerFormatString = "{0}\n{3}：{4:P1}",
                XAxisKey = xKey,
                YAxisKey = yOeeKey,
                Color = ToOxy(opRgb),
                StrokeThickness = 2.8,
                LineStyle = LineStyle.Solid,
                MarkerType = MarkerType.Triangle,
                MarkerSize = 5.5,
                MarkerFill = ToOxy(t.ChartSurface),
                MarkerStroke = ToOxy(opRgb),
                MarkerStrokeThickness = 2
            };
            for (int i = 0; i < stats.Count; i++)
            {
                var y = operatorTrend![i];
                if (y.HasValue)
                {
                    sOpTrend.Points.Add(new DataPoint(i, y.Value));
                    maxOee = Math.Max(maxOee, y.Value);
                    minOee = Math.Min(minOee, y.Value);
                }
                else
                    sOpTrend.Points.Add(DataPoint.Undefined);
            }

            ApplySmoothOeeLine(sOpTrend);
        }

        yMinutes.Maximum = maxMinutes <= 0 ? double.NaN : maxMinutes * 1.15;
        yOee.Minimum = minOee != double.MaxValue && minOee >= 0.5
            ? Math.Max(0, Math.Floor((minOee - 0.05) * 10) / 10)
            : 0;
        yOee.Maximum = maxOee <= 0 ? 1 : Math.Max(maxOee * 1.12, 1.02);

        if (stats.Count > 0)
        {
            double labelCollisionDistance = (yOee.Maximum - yOee.Minimum) * 0.08;
            OxyColor surfaceColor = ToOxy(t.ChartSurface);
            OxyColor overallColor = ToOxy(t.ChartLineOee);
            OxyColor operatorColor = ToOxy(Color.FromRgb(232, 121, 249));

            for (int i = 0; i < stats.Count; i++)
            {
                double? overall = stats[i].Oee;
                double? selectedOperator = showOperatorTrend ? operatorTrend![i] : null;
                bool labelsAreClose = overall.HasValue
                    && selectedOperator.HasValue
                    && Math.Abs(overall.Value - selectedOperator.Value) <= labelCollisionDistance;

                if (overall.HasValue)
                {
                    // 两条线靠近时，把数值分置在线条外侧，避免同月标签互相覆盖。
                    bool placeAbove = !labelsAreClose || overall.Value >= selectedOperator!.Value;
                    AddOeeValueLabel(model, i, overall.Value, overallColor, surfaceColor, placeAbove);
                }

                if (selectedOperator.HasValue)
                {
                    bool placeAbove = !labelsAreClose || selectedOperator.Value > overall!.Value;
                    AddOeeValueLabel(model, i, selectedOperator.Value, operatorColor, surfaceColor, placeAbove);
                }
            }
        }

        model.Series.Add(sEff);
        model.Series.Add(sAct);
        model.Series.Add(sOee);
        if (sOpTrend != null)
            model.Series.Add(sOpTrend);

        PlotMonth.Model = model;
    }

    private static void ApplyPlotTheme(PlotModel model, UiTheme t)
    {
        model.Background = ToOxy(t.ChartSurface);
        model.PlotAreaBackground = ToOxy(t.ChartSurface);
        model.PlotAreaBorderColor = ToOxy(t.ChartBorder);
        model.TextColor = ToOxy(t.ChartLegendFg);
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
        model.InvalidatePlot(false);
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

    private void BtnExportPerfMonth_Click(object sender, RoutedEventArgs e)
    {
        if (CboPerfMonth.SelectedItem is not string monthKey || string.IsNullOrWhiteSpace(monthKey))
        {
            MessageBox.Show(this, "请先在「绩效表月份」中选择要导出的月份。", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var records = _repo.LoadRecordsForMonths(new HashSet<string>(StringComparer.Ordinal) { monthKey });
        var stats = PerformanceAggregator.AggregateByOperator(records).ToList();
        if (stats.Count == 0)
        {
            MessageBox.Show(this, $"月份「{monthKey}」暂无绩效数据。", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        double avg = PerformanceAggregator.AverageEffectiveMinutesPerOperator(records);
        var dlg = new SaveFileDialog
        {
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            FileName = $"绩效表_{monthKey}_{DateTime.Now:yyyyMMdd_HHmm}",
            DefaultExt = ".xlsx",
            AddExtension = true
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            MonthlyPerformanceExporter.ExportToXlsx(dlg.FileName, monthKey, stats, avg);
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
