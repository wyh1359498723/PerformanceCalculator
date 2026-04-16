using System.Windows;

namespace PerformanceCalculator2;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        SQLitePCL.Batteries_V2.Init();
        base.OnStartup(e);
    }
}

