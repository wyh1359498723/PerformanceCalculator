using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PerformanceCalculator2;

public partial class HistoryWindow : Window
{
    private readonly HistoryRepository _repo;
    private readonly ObservableCollection<DataSourceRowVm> _rows = new();

    public bool HasChanges { get; private set; }

    public HistoryWindow(HistoryRepository repo)
    {
        _repo = repo;
        InitializeComponent();
        GridBatches.ItemsSource = _rows;
        Loaded += (_, _) =>
        {
            ReloadList();
            StyleHistoryGrid();
        };
    }

    private void StyleHistoryGrid()
    {
        var t = UiTheme.Tech;
        var dg = GridBatches;
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
        headerStyle.Setters.Add(new Setter(FontWeightProperty, FontWeights.SemiBold));
        headerStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(8, 6, 8, 6)));
        headerStyle.Setters.Add(new Setter(CursorProperty, System.Windows.Input.Cursors.Hand));
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

    private void ReloadList()
    {
        _rows.Clear();
        foreach (var b in _repo.ListDataSources())
        {
            var local = b.CreatedUtc.ToLocalTime();
            _rows.Add(new DataSourceRowVm
            {
                Id = b.Id,
                CreatedLocalValue = local,
                MonthKey = b.MonthKey,
                RowCountValue = b.RowCount,
                SourceFile = b.SourceFile ?? ""
            });
        }
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        // 提交当前单元格的编辑，确保刚点击的复选框值已写回 ViewModel。
        GridBatches.CommitEdit(DataGridEditingUnit.Cell, true);
        GridBatches.CommitEdit(DataGridEditingUnit.Row, true);
        var sel = _rows.Where(r => r.IsSelected).ToList();
        if (sel.Count == 0)
        {
            MessageBox.Show(this, "请先勾选要删除的数据源。", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"确定删除已勾选的 {sel.Count} 个数据源？对应绩效明细也会被删除，此操作不可恢复。", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _repo.DeleteDataSources(sel.Select(r => r.Id));
        HasChanges = true;

        ReloadList();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    public sealed class DataSourceRowVm
    {
        public long Id { get; init; }
        public bool IsSelected { get; set; }
        public DateTime CreatedLocalValue { get; init; }
        public string CreatedLocal => CreatedLocalValue.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        public string MonthKey { get; init; } = "";
        public int RowCountValue { get; init; }
        public string RowCount => RowCountValue.ToString(CultureInfo.InvariantCulture);
        public string SourceFile { get; init; } = "";
    }
}
