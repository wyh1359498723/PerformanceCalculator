using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PerformanceCalculator2;

public sealed class SelectableMonth : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public SelectableMonth(string monthKey) => MonthKey = monthKey;

    public string MonthKey { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
