using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace PerformanceCalculator2;

public partial class HistoryWindow : Window
{
    private readonly HistoryRepository _repo;
    private readonly ObservableCollection<BatchRowVm> _rows = new();

    public HistoryWindow(HistoryRepository repo)
    {
        _repo = repo;
        InitializeComponent();
        GridBatches.ItemsSource = _rows;
        Loaded += (_, _) => ReloadList();
    }

    private void ReloadList()
    {
        _rows.Clear();
        foreach (var b in _repo.ListBatches())
        {
            var local = b.CreatedUtc.ToLocalTime();
            _rows.Add(new BatchRowVm
            {
                Id = b.Id,
                CreatedLocal = local.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
                MonthKey = b.MonthKey,
                RowCount = b.RowCount.ToString(CultureInfo.InvariantCulture),
                SourceFiles = b.SourceFiles ?? ""
            });
        }
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        var sel = GridBatches.SelectedItems.Cast<BatchRowVm>().ToList();
        if (sel.Count == 0)
        {
            MessageBox.Show(this, "请先选择要删除的批次。", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"确定删除选中的 {sel.Count} 个导入批次？此操作不可恢复。", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        foreach (var r in sel)
            _repo.DeleteBatch(r.Id);

        ReloadList();
        DialogResult = true;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    public sealed class BatchRowVm
    {
        public long Id { get; init; }
        public string CreatedLocal { get; init; } = "";
        public string MonthKey { get; init; } = "";
        public string RowCount { get; init; } = "";
        public string SourceFiles { get; init; } = "";
    }
}
