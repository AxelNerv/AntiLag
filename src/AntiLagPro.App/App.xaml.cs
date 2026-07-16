using System.Threading;
using System.Windows;

namespace AntiLagPro.App;

public partial class App : Application
{
    private Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Второй экземпляр не даёт пользы (два трея, борьба за таймер) — не пускаем.
        _single = new Mutex(true, @"Global\AntiLag_AxelNerv_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("AntiLag уже запущен — ищи значок в трее (возле часов).",
                "AntiLag", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }
}
