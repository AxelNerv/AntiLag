using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AntiLagPro.Core;

namespace AntiLagPro.App;

public partial class MainWindow : Window
{
    private readonly TweakEngine _engine = new();
    private readonly LatencyMeter _meter = new();
    private readonly DiagnosticsEngine _diag = new();
    private readonly ConnectionMonitor _monitor = new();
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly ObservableCollection<TweakRow> _rows = new();       // базовые (Universal)
    private readonly ObservableCollection<TweakRow> _gameRows = new();   // игровые (Game)
    private readonly ObservableCollection<FindingRow> _findings = new();
    private readonly ObservableCollection<DnsRow> _dnsRows = new();
    private readonly ObservableCollection<DriverRow> _driverRows = new();
    private readonly ObservableCollection<DriverUpdate> _driverUpdates = new();
    private readonly SystemMonitor _sysmon = new();
    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _exiting;
    private bool _initializing = true;
    private readonly bool _startHidden = Environment.GetCommandLineArgs()
        .Any(a => string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase));

    private IEnumerable<TweakRow> AllRows => _rows.Concat(_gameRows);

    private static readonly Brush Green  = new SolidColorBrush(Color.FromRgb(0x1F, 0x9E, 0x58));
    private static readonly Brush Yellow = new SolidColorBrush(Color.FromRgb(0xD9, 0xA5, 0x3A));
    private static readonly Brush Red    = new SolidColorBrush(Color.FromRgb(0xD6, 0x45, 0x45));

    public MainWindow()
    {
        InitializeComponent();

        TweaksItems.ItemsSource = _rows;
        GameTweaksItems.ItemsSource = _gameRows;
        FindingsItems.ItemsSource = _findings;
        DnsItems.ItemsSource = _dnsRows;
        DriverItems.ItemsSource = _driverRows;
        WuItems.ItemsSource = _driverUpdates;
        foreach (var s in DnsOptimizer.GetAllServers()) _dnsRows.Add(new DnsRow(s));
        UpdateCurrentDns();
        LoadSummary();
        BackupPathText.Text = "Бэкап хранится в: " + BackupStore.Location;
        LoadRows();

        // Держим 0.5 ms пока окно открыто (как делал оригинальный AntiLag).
        _engine.Timer.Start();
        HoldTimerCheck.IsChecked = true;
        AutoStartCheck.IsChecked = AutoStart.IsEnabled();
        MinTrayCheck.IsChecked = Settings.MinimizeToTray;

        _uiTimer.Tick += (_, _) => UpdateStatus();
        _uiTimer.Start();
        UpdateStatus();

        Closed += (_, _) => { _meter.Stop(); _monitor.Stop(); _engine.Timer.Stop(); };
        InitTray();
        _initializing = false;
    }

    // --- Трей-режим ---
    private void InitTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon { Text = "AntiLag — держит таймер 0.5 ms", Visible = true };
        try
        {
            var s = System.Windows.Application.GetResourceStream(new Uri("Resources/tray.ico", UriKind.Relative))?.Stream;
            if (s is not null) _tray.Icon = new System.Drawing.Icon(s);
        }
        catch { /* иконка не критична */ }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => ShowFromTray());
        menu.Items.Add("Выход", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
        Topmost = true; Topmost = false;   // вытащить поверх
    }

    private void ExitApp()
    {
        _exiting = true;
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // При автозапуске с Windows не показываем окно — сразу прячемся в трей,
        // фон держит 0.5 ms. Открыть можно из меню трея.
        if (_startHidden)
        {
            Hide();
            ShowInTaskbar = false;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exiting && Settings.MinimizeToTray)
        {
            e.Cancel = true;        // не закрываем — прячем в трей, фон продолжает держать 0.5 ms
            Hide();
            ShowInTaskbar = false;
        }
        else
        {
            if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            base.OnClosing(e);
        }
    }

    private void MinTray_Checked(object sender, RoutedEventArgs e) { if (!_initializing) Settings.MinimizeToTray = true; }
    private void MinTray_Unchecked(object sender, RoutedEventArgs e) { if (!_initializing) Settings.MinimizeToTray = false; }

    private void LoadRows()
    {
        _rows.Clear();
        _gameRows.Clear();
        foreach (var s in _engine.GetStatus())
        {
            var row = new TweakRow(s.Tweak, s.IsApplied);
            if (s.Tweak.Tier == TweakTier.Game) _gameRows.Add(row);
            else _rows.Add(row);
        }
    }

    private void UpdateStatus()
    {
        TimerText.Text = $"{_engine.Timer.CurrentMs:N4} ms";
        AdminText.Text = TweakEngine.IsElevated() ? "права администратора: есть" : "БЕЗ прав администратора!";

        if (_meter.IsRunning)
        {
            MeterCurrent.Text = $"{_meter.CurrentUs:N0} µs";
            MeterMax.Text     = $"{_meter.MaxUs:N0} µs";
            MeterCurrent.Foreground = ColorFor(_meter.CurrentUs);
            MeterMax.Foreground     = ColorFor(_meter.MaxUs);
        }

        if (_monitor.IsRunning)
        {
            MonStatus.Text   = _monitor.Status;
            MonLatency.Text  = $"{_monitor.AvgLatency:N0} мс";
            MonLoss.Text     = $"{_monitor.LossPercent:N1} %";
            MonJitter.Text   = $"{_monitor.Jitter:N0} мс";
            MonPeaks.Text    = _monitor.Peaks.ToString();
            var c = LevelColor(_monitor.StatusLevel);
            MonStatus.Foreground = c;
            MonStatusPill.Background = LevelPill(_monitor.StatusLevel);
        }

        var mem = MemoryCleaner.GetStatus();
        MemText.Text = $"Использовано {mem.usedGB:N1} ГБ из {mem.totalGB:N1} ГБ  (свободно {mem.freeGB:N1} ГБ)";
        MemBar.Value = mem.loadPercent;

        // Живые плитки «О системе» — только когда вкладка активна (экономим ресурсы).
        if (Nav.SelectedItem is TabItem st && (st.Header as string) == "О системе")
            UpdateSystemTiles();
    }

    private static Brush ColorFor(double us)
        => us >= 1000 ? Red : us >= 500 ? Yellow : Green;

    private static Brush LevelColor(int level)
        => level >= 2 ? Red : level == 1 ? Yellow : Green;

    private static Brush LevelPill(int level)
        => level >= 2 ? new SolidColorBrush(Color.FromRgb(0x2A, 0x16, 0x18))
         : level == 1 ? new SolidColorBrush(Color.FromRgb(0x2A, 0x22, 0x10))
         :              new SolidColorBrush(Color.FromRgb(0x10, 0x22, 0x18));

    private void MeterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_meter.IsRunning)
        {
            _meter.Stop();
            MeterButton.Content = "Включить";
        }
        else
        {
            _meter.Start();
            MeterButton.Content = "Остановить";
        }
    }

    private void MeterReset_Click(object sender, RoutedEventArgs e)
    {
        _meter.ResetMax();
        MeterMax.Text = "0 µs";
        MeterMax.Foreground = Green;
        if (!_meter.IsRunning) MeterCurrent.Text = "—";
    }

    // --- О системе (бенто-дашборд) ---
    private async void LoadSummary()
    {
        try
        {
            var s = await Task.Run(() => SystemInfo.GetSummary());
            SysBoard.Text   = s.Board;
            SysCpu.Text     = s.Cpu;
            SysGpu.Text     = s.Gpu;
            SysRam.Text     = $"{s.RamGB:N0} ГБ";
            SysStorage.Text = $"{s.StorageTB:N2} ТБ";
            SysWin.Text     = s.Windows;
            SysWei.Text     = s.Wei;
        }
        catch { /* не критично */ }
    }

    private void SysInfo_Click(object sender, RoutedEventArgs e) => LoadSummary();

    private void UpdateSystemTiles()
    {
        SysUptime.Text = _sysmon.Uptime();
        TileCpu.Text  = $"{_sysmon.CpuPercent():N0} %";
        TileMem.Text  = $"{_sysmon.MemPercent()} %";
        TileProc.Text = _sysmon.Processes().ToString();
        var net = _sysmon.Network();
        TileNet.Text = $"{net.totalGB:N1} ГБ";
        TileNetSpeed.Text = $"↑ {SystemMonitor.Speed(net.up)}   ↓ {SystemMonitor.Speed(net.down)}";
    }

    // Сканер диагностики — работает на любом ПК, ничего не меняет.
    private bool _diagScanning;

    private async void ScanButton_Click(object sender, RoutedEventArgs e) => await RunDiagScan();

    private async Task RunDiagScan()
    {
        if (_diagScanning) return;
        _diagScanning = true;
        ScanButton.IsEnabled = false;
        ScanButton.Content = "Сканирую…";
        _findings.Clear();
        try
        {
            var results = await Task.Run(() => _diag.Scan());
            foreach (var f in results)
                _findings.Add(FindingRow.From(f));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка диагностики", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScanButton.IsEnabled = true;
            ScanButton.Content = "Сканировать систему";
            _diagScanning = false;
        }
    }

    // Авто-скан при первом открытии вкладки «Диагностика».
    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Nav)) return; // игнорируем события дочерних ComboBox
        if (Nav.SelectedItem is TabItem ti && ti.Header is string h && h == "Диагностика"
            && _findings.Count == 0 && !_diagScanning)
            _ = RunDiagScan();
    }

    // Кнопка «Открыть →» у находки — переход на нужную вкладку.
    private void FindingGoTo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string header || header.Length == 0) return;
        foreach (var item in Nav.Items)
            if (item is TabItem ti && ti.Header is string h && h == header)
            {
                ti.IsSelected = true;
                break;
            }
    }

    private void RefreshApplied()
    {
        var status = _engine.GetStatus().ToDictionary(s => s.Tweak.Id, s => s.IsApplied);
        foreach (var row in AllRows)
            if (status.TryGetValue(row.Id, out bool applied))
                row.IsApplied = applied;
    }

    // "Применить выбранное" = синхронизировать систему с галочками:
    //   отмечено и не применено -> применить;  снято и применено -> откатить.
    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var toApply   = AllRows.Where(r =>  r.IsSelected && !r.IsApplied).Select(r => r.Id).ToList();
            var toRestore = AllRows.Where(r => !r.IsSelected &&  r.IsApplied).Select(r => r.Id).ToList();

            if (toApply.Count > 0)   _engine.Apply(toApply);
            if (toRestore.Count > 0) _engine.Restore(toRestore);

            RefreshApplied();
            ShowRebootIfNeeded(toApply.Count > 0 && _engine.RequiresRebootAfter);

            MessageBox.Show(this, "Готово. Изменения применены.", "AntiLag",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _engine.Restore();                 // откатить всё, что есть в бэкапе
            foreach (var row in AllRows) row.IsSelected = false;
            RefreshApplied();
            RebootBox.Visibility = Visibility.Collapsed;

            MessageBox.Show(this, "Все изменения откатаны — система в исходном состоянии.",
                "AntiLag", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowRebootIfNeeded(bool needed)
    {
        RebootBox.Visibility = needed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HoldTimer_Checked(object sender, RoutedEventArgs e) => _engine.Timer.Start();
    private void HoldTimer_Unchecked(object sender, RoutedEventArgs e) => _engine.Timer.Stop();

    // --- Настройки ---
    private void AutoStart_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initializing) AutoStart.Set(true);
    }

    private void AutoStart_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_initializing) AutoStart.Set(false);
    }

    private void WindowSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is ComboBoxItem item && item.Tag is string tag)
        {
            var p = tag.Split(',');
            if (p.Length == 2 && double.TryParse(p[0], out double w) && double.TryParse(p[1], out double h))
            {
                Width = w;
                Height = h;
            }
        }
    }

    // --- Мониторинг соединения ---
    private void MonitorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_monitor.IsRunning)
        {
            _monitor.Stop();
            MonitorButton.Content = "Включить мониторинг";
            MonStatus.Text = "выключено";
            MonStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x85, 0x8E, 0x9E));
            MonStatusPill.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x26));
            MonLatency.Text = MonLoss.Text = MonJitter.Text = MonPeaks.Text = "—";
        }
        else
        {
            _monitor.Start("8.8.8.8");
            MonitorButton.Content = "Выключить";
        }
    }

    // --- Тест скорости ---
    private async void SpeedTest_Click(object sender, RoutedEventArgs e)
    {
        SpeedTestButton.IsEnabled = false;
        SpeedTestButton.Content = "Тест…";
        SpeedDown.Text = SpeedUp.Text = SpeedPing.Text = "…";
        string tag = (SpeedServerCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "selectel";
        var server = tag == "cf" ? SpeedServer.Cloudflare : SpeedServer.SelectelRu;
        var log = new Progress<string>(s => SpeedStatus.Text = s);
        try
        {
            var res = await Task.Run(() => SpeedTest.Run(server, log));
            if (res.Ok)
            {
                SpeedDown.Text = $"{res.DownMbps:N0} Мбит/с";
                SpeedUp.Text   = $"{res.UpMbps:N0} Мбит/с";
                SpeedPing.Text = res.PingMs >= 0 ? $"{res.PingMs} мс" : "—";
                SpeedStatus.Text = "Готово.";
            }
            else
            {
                SpeedDown.Text = SpeedUp.Text = SpeedPing.Text = "—";
                SpeedStatus.Text = "Ошибка: " + res.Error;
            }
        }
        finally
        {
            SpeedTestButton.IsEnabled = true;
            SpeedTestButton.Content = "Запустить";
        }
    }

    // --- DNS Optimizer ---
    private void UpdateCurrentDns()
    {
        var dns = NetworkTools.GetActiveDns();
        DnsCurrentText.Text = "Текущий DNS: " + (dns.Length > 0 ? string.Join(", ", dns) : "авто (DHCP)");
    }

    private async void DnsMeasure_Click(object sender, RoutedEventArgs e)
    {
        DnsMeasureButton.IsEnabled = false;
        DnsMeasureButton.Content = "Замеряю…";
        try
        {
            var measures = await Task.Run(() => DnsOptimizer.MeasureAll());
            _dnsRows.Clear();
            foreach (var m in measures) _dnsRows.Add(new DnsRow(m.Server, m.AvgMs));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ошибка"); }
        finally
        {
            DnsMeasureButton.IsEnabled = true;
            DnsMeasureButton.Content = "Обновить отклики";
        }
    }

    private async void DnsApply_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not DnsRow row) return;

        // «Системный» = вернуть DNS роутера (DHCP).
        if (row.Server.IsSystem)
        {
            DnsReset_Click(sender, e);
            return;
        }

        var res = MessageBox.Show(this,
            $"Установить DNS {row.Name} ({row.Server.Primary} / {row.Server.Secondary}) на активном адаптере?\n\n" +
            "Если DNS управляется роутером/VPN — может конфликтовать. Откат: «Сбросить на DHCP».",
            "Сменить DNS", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (res != MessageBoxResult.OK) return;
        DnsCurrentText.Text = "Применяю DNS…";
        try
        {
            await Task.Run(() => DnsOptimizer.Apply(row.Server));
            UpdateCurrentDns();
            MessageBox.Show(this, $"DNS {row.Name} установлен.", "AntiLag", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { UpdateCurrentDns(); MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void DnsReset_Click(object sender, RoutedEventArgs e)
    {
        DnsCurrentText.Text = "Сбрасываю DNS…";
        try
        {
            await Task.Run(() => DnsOptimizer.Reset());
            UpdateCurrentDns();
            MessageBox.Show(this, "DNS сброшен на автоматический (DHCP).", "AntiLag", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { UpdateCurrentDns(); MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    // --- Драйверы ---
    private async void DriverScan_Click(object sender, RoutedEventArgs e)
    {
        DriverScanButton.IsEnabled = false;
        DriverScanButton.Content = "Сканирую…";
        _driverRows.Clear();
        try
        {
            var drivers = await Task.Run(() => DriverChecker.ScanDrivers());
            foreach (var d in drivers) _driverRows.Add(new DriverRow(d));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ошибка"); }
        finally
        {
            DriverScanButton.IsEnabled = true;
            DriverScanButton.Content = "Сканировать драйверы";
        }
    }

    private async void DriverWu_Click(object sender, RoutedEventArgs e)
    {
        DriverWuButton.IsEnabled = false;
        DriverWuButton.Content = "Проверяю…";
        WuPanel.Visibility = Visibility.Visible;
        WuInstallAll.Visibility = Visibility.Collapsed;
        WuStatus.Text = "Запрос к Windows Update (может занять до минуты)…";
        _driverUpdates.Clear();
        try
        {
            var ups = await Task.Run(() => DriverChecker.GetDriverUpdates());
            foreach (var u in ups) _driverUpdates.Add(u);
            WuStatus.Text = ups.Count == 0 ? "Windows Update: новых драйверов не предлагает." : $"Доступно обновлений: {ups.Count}";
            WuInstallAll.Visibility = ups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { WuStatus.Text = "Ошибка: " + ex.Message; }
        finally
        {
            DriverWuButton.IsEnabled = true;
            DriverWuButton.Content = "Проверить Windows Update";
        }
    }

    private async void WuInstall_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is DriverUpdate u) await InstallWu(new[] { u });
    }

    private async void WuInstallAll_Click(object sender, RoutedEventArgs e)
        => await InstallWu(_driverUpdates.ToList());

    private async Task InstallWu(System.Collections.Generic.IList<DriverUpdate> ups)
    {
        if (ups.Count == 0) return;
        WuInstallAll.IsEnabled = false;
        DriverWuButton.IsEnabled = false;
        var log = new Progress<string>(s => WuStatus.Text = s);
        try
        {
            string result = await Task.Run(() => DriverChecker.InstallUpdates(ups, log));
            var rest = await Task.Run(() => DriverChecker.GetDriverUpdates());
            _driverUpdates.Clear();
            foreach (var u in rest) _driverUpdates.Add(u);
            WuStatus.Text = result + (rest.Count == 0 ? "  Обновлений больше нет." : $"  Осталось: {rest.Count}");
            WuInstallAll.Visibility = rest.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { WuStatus.Text = "Ошибка установки: " + ex.Message; }
        finally
        {
            WuInstallAll.IsEnabled = true;
            DriverWuButton.IsEnabled = true;
        }
    }

    private void DriverLink_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string url || url.Length == 0) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ошибка"); }
    }

    // --- Очистка ОЗУ ---
    private async void CleanRam_Click(object sender, RoutedEventArgs e)
    {
        CleanRamButton.IsEnabled = false;
        CleanRamButton.Content = "Очистка…";
        MemResult.Text = "";
        try
        {
            double freed = await Task.Run(() => MemoryCleaner.Clean());
            MemResult.Text = $"Освобождено ~{freed:N0} МБ";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ошибка"); }
        finally
        {
            CleanRamButton.IsEnabled = true;
            CleanRamButton.Content = "Очистить память";
        }
    }

    // --- Сеть ---
    private async void NetStart_Click(object sender, RoutedEventArgs e)
    {
        string target = NetTarget.Text.Trim();
        if (target.Length == 0) { MessageBox.Show(this, "Укажи хост или IP."); return; }

        string test = (NetTestCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "ping";
        int port = int.TryParse(NetPort.Text, out int pp) ? pp : 443;

        NetOutput.Clear();
        NetStartButton.IsEnabled = false;
        NetStartButton.Content = "Идёт тест…";
        var log = new Progress<string>(line => { NetOutput.AppendText(line + "\r\n"); NetOutput.ScrollToEnd(); });
        try
        {
            await Task.Run(() =>
            {
                switch (test)
                {
                    case "tcp":   NetworkTools.TcpPing(target, port, log); break;
                    case "trace": NetworkTools.TraceRoute(target, log);    break;
                    default:      NetworkTools.Ping(target, log);          break;
                }
            });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ошибка"); }
        finally
        {
            NetStartButton.IsEnabled = true;
            NetStartButton.Content = "Начать тест";
        }
    }

    private async void NetScan_Click(object sender, RoutedEventArgs e)
    {
        NetOutput.Clear();
        NetScanButton.IsEnabled = false;
        NetScanButton.Content = "Сканирую…";
        try
        {
            var findings = await Task.Run(() => NetworkTools.ScanNetwork());
            foreach (var f in findings)
            {
                string mark = f.Severity switch
                {
                    Severity.Bad => "[!]",
                    Severity.Warn => "[~]",
                    Severity.Ok => "[ok]",
                    _ => "[i]"
                };
                NetOutput.AppendText($"{mark} {f.Title} — {f.Detail}\r\n");
                if (!string.IsNullOrEmpty(f.Fix)) NetOutput.AppendText($"      → {f.Fix}\r\n");
            }
            NetOutput.ScrollToEnd();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ошибка"); }
        finally
        {
            NetScanButton.IsEnabled = true;
            NetScanButton.Content = "Сканировать сеть";
        }
    }
}
