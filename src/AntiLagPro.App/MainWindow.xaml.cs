using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AntiLagPro.Core;

namespace AntiLagPro.App;

public partial class MainWindow : Window, IDisposable
{
    private readonly TweakEngine _engine = new();
    private readonly LatencyMeter _meter = new();
    private readonly DiagnosticsEngine _diag = new();
    private readonly ConnectionMonitor _monitor = new();
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly ObservableCollection<TweakRow> _rows = new();       // базовые (Universal)
    private readonly ObservableCollection<TweakRow> _gameRows = new();   // игровые (Game)
    private readonly ObservableCollection<TweakRow> _lookRows = new();   // оформление (Appearance)
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

    private IEnumerable<TweakRow> AllRows => _rows.Concat(_gameRows).Concat(_lookRows);

    // Статусные цвета — семантика, а не бренд: зелёный/жёлтый/красный остаются.
    private static readonly Brush Green  = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly Brush Yellow = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly Brush Red    = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

    private bool _hiddenOnce;

    public MainWindow()
    {
        InitializeComponent();

        // Автозапуск: рисуем окно ЗА экраном (иначе на старте Windows первая
        // отрисовка WPF выходит чёрной). Спрячем в трей уже после первого рендера.
        if (_startHidden)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000; Top = -32000;
            ShowInTaskbar = false;
        }

        TweaksItems.ItemsSource = _rows;
        LookTweaksItems.ItemsSource = _lookRows;
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
        AutoUpdateCheck.IsChecked = Settings.AutoUpdate;
        DenseCheck.IsChecked = Settings.DenseRows;
        ApplyDensity(Settings.DenseRows);

        _uiTimer.Tick += (_, _) => UpdateStatus();
        _uiTimer.Start();
        UpdateStatus();

        Closed += (_, _) => { _meter.Stop(); _monitor.Stop(); _engine.Timer.Stop(); };
        InitTray();
        BuildNav();
        BuildSwatches();
        LoadCursors();
        InitGlass();
        ShowVersion();
        _initializing = false;
        _ = CheckUpdatesAsync();
    }

    // --- Оформление: стеклянный проводник ---
    private void InitGlass()
    {
        int kind = Settings.GlassKind;
        (kind switch
        {
            2 => GlassMica, 3 => GlassAcrylic, 4 => GlassTabbed, 11 => GlassTint, _ => GlassBlur
        }).IsChecked = true;

        GlassAlphaSlider.Value = Settings.GlassAlpha;
        GlassAlphaText.Text = AlphaPercent(Settings.GlassAlpha);
        UpdateGlassColorButton();
        GlassCheck.IsChecked = Settings.Glass;
        UpdateGlassRowState();
        if (Settings.Glass) ApplyGlass();
    }

    private static string AlphaPercent(double a) => $"{a / 255.0 * 100:N0} %";

    private void UpdateGlassColorButton()
    {
        if (Theme.TryParse(Settings.GlassColor, out var c))
            GlassColorButton.Background = new SolidColorBrush(c);
    }

    /// <summary>Цвет и прозрачность имеют смысл только для «Размытия» и «Заливки».</summary>
    private void UpdateGlassRowState()
    {
        int kind = Settings.GlassKind;
        bool custom = kind is 10 or 11;
        GlassTintRow.IsEnabled = custom;
        GlassTintRow.Opacity = custom ? 1.0 : 0.45;
    }

    private static void ApplyGlass()
    {
        if (!Theme.TryParse(Settings.GlassColor, out var c))
        {
            Log.Warn($"Не разобрать цвет стекла «{Settings.GlassColor}», беру серый по умолчанию");
            c = System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x26);
        }
        GlassEffect.Enable((GlassKind)Settings.GlassKind, c.R, c.G, c.B, (byte)Settings.GlassAlpha);
    }

    private void Glass_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        Settings.Glass = true;
        ApplyGlass();
        if (!GlassEffect.SystemTransparency)
            GlassHint.Text = "В Windows выключены «Эффекты прозрачности» — без них эффект не виден. Включи их в Параметрах → Персонализация → Цвета.";
    }

    private void Glass_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        Settings.Glass = false;
        GlassEffect.Disable();
    }

    private void GlassKind_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing || sender is not System.Windows.Controls.RadioButton rb) return;
        if (!int.TryParse(rb.Tag as string, out int kind)) return;
        Settings.GlassKind = kind;
        UpdateGlassRowState();
        if (Settings.Glass) ApplyGlass();
    }

    private void GlassColor_Click(object sender, RoutedEventArgs e)
    {
        if (!Theme.TryParse(Settings.GlassColor, out var cur))
            cur = System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x26);
        var dlg = new ColorPickerWindow(cur) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        Settings.GlassColor = Theme.ToHex(dlg.SelectedColor);
        UpdateGlassColorButton();
        if (Settings.Glass) ApplyGlass();
    }

    /// <summary>
    /// Пока тянут ползунок, эффект применяем с задержкой: обход всех окон
    /// проводника на каждое движение мыши подвешивал перетаскивание.
    /// </summary>
    private DispatcherTimer? _glassDelay;

    private void GlassAlpha_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (GlassAlphaText is null) return;
        GlassAlphaText.Text = AlphaPercent(e.NewValue);
        if (_initializing) return;

        Settings.GlassAlpha = (int)e.NewValue;
        if (!Settings.Glass) return;

        _glassDelay ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _glassDelay.Tick -= GlassDelayTick;
        _glassDelay.Tick += GlassDelayTick;
        _glassDelay.Stop();
        _glassDelay.Start();
    }

    private void GlassDelayTick(object? sender, EventArgs e)
    {
        _glassDelay?.Stop();
        ApplyGlass();
    }

    // --- Оформление: схемы курсоров ---
    private void LoadCursors()
    {
        try
        {
            CursorItems.ItemsSource = CursorSchemes.All();
            string cur = CursorSchemes.Current;
            CursorStatus.Text = string.IsNullOrWhiteSpace(cur) ? "Сейчас: системные курсоры" : $"Сейчас: {cur}";
        }
        catch (Exception ex) { CursorStatus.Text = "Ошибка: " + ex.Message; }
    }

    private void CursorApply_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not CursorScheme s) return;
        try
        {
            var backup = BackupStore.Load();
            CursorSchemes.Apply(s, backup);
            BackupStore.Save(backup);
            CursorStatus.Text = $"Сейчас: {s.Name}";
            CursorStatus.Foreground = Green;
        }
        catch (Exception ex)
        {
            CursorStatus.Text = "Не вышло: " + ex.Message;
            CursorStatus.Foreground = Red;
        }
    }

    private void CursorRestore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var backup = BackupStore.Load();
            CursorSchemes.RestoreSystem(backup);
            BackupStore.Save(backup);
            CursorStatus.Text = "Сейчас: системные курсоры";
            CursorStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
        }
        catch (Exception ex) { CursorStatus.Text = "Не вышло: " + ex.Message; CursorStatus.Foreground = Red; }
    }

    private void CursorRefresh_Click(object sender, RoutedEventArgs e) => LoadCursors();

    private void CursorFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(CursorSchemes.Root);
            Process.Start(new ProcessStartInfo(CursorSchemes.Root) { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>Версия берётся из сборки — правим только csproj, в UI подставляется сама.</summary>
    private void ShowVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        VersionSide.Text = VersionAbout.Text = "v" + v.ToString(3);
    }

    // --- Выбор акцентного цвета ---
    private Button? _customSwatch;

    private void BuildSwatches()
    {
        var style = (Style)FindResource("Swatch");
        SwatchRow.Children.Clear();

        foreach (var (name, color) in Theme.Presets)
        {
            var b = new Button
            {
                Style = style,
                Background = new SolidColorBrush(color),
                Tag = Theme.ToHex(color),
                ToolTip = name,
            };
            b.Click += Accent_Click;
            SwatchRow.Children.Add(b);
        }

        _customSwatch = new Button
        {
            Style = style,
            ToolTip = "Свой цвет",
            Content = new TextBlock
            {
                Text = "\uE790",   // палитра (Segoe Fluent Icons)
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 15,
            },
        };
        _customSwatch.Click += AccentCustom_Click;
        SwatchRow.Children.Add(_customSwatch);

        UpdateSwatches();
    }

    /// <summary>Подсвечивает выбранный образец и обновляет плитку «свой цвет».</summary>
    private void UpdateSwatches()
    {
        if (_customSwatch is null) return;
        var cur = Theme.Current;
        bool isPreset = Theme.Presets.Any(p => p.Color == cur);
        var ring = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));

        for (int i = 0; i < Theme.Presets.Length; i++)
            if (SwatchRow.Children[i] is Button b)
                b.BorderBrush = Theme.Presets[i].Color == cur ? ring : Brushes.Transparent;

        // своя плитка: показывает выбранный цвет, если он не из пресетов
        _customSwatch.Background = new SolidColorBrush(isPreset ? Color.FromRgb(0x3F, 0x3F, 0x46) : cur);
        _customSwatch.BorderBrush = isPreset ? Brushes.Transparent : ring;
        if (_customSwatch.Content is TextBlock t)
            t.Foreground = new SolidColorBrush(isPreset
                ? Color.FromRgb(0xFA, 0xFA, 0xFA)
                : ((SolidColorBrush)Application.Current.Resources["AccentFg"]).Color);
    }

    private void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && Theme.TryParse(b.Tag as string, out var c)) { Theme.Apply(c); UpdateSwatches(); }
    }

    private void AccentCustom_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ColorPickerWindow(Theme.Current) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        Theme.Apply(dlg.SelectedColor);
        UpdateSwatches();
    }

    // --- Боковая навигация ---
    /// <summary>Пункт меню. Имена свойств совпадают с TabItem — шаблон NavItem общий.</summary>
    private sealed record NavEntry(string Header, string Tag);

    /// <summary>
    /// Строится из самих вкладок, чтобы не дублировать названия. Сами TabItem в
    /// ListBox класть нельзя — у элемента не может быть двух родителей.
    /// </summary>
    private sealed record NavLink(string Header, string Tag, int Index);

    private static readonly string[] GroupA = { "Базовое", "Игровые", "Ускорение" };
    private static readonly string[] GroupB = { "Сеть", "DNS", "DPC-метр", "Диагностика", "Драйверы" };

    private ListBox[] NavLists => new[] { NavA, NavB, NavC };
    private bool _navSync;

    private void BuildNav()
    {
        var all = Nav.Items.OfType<TabItem>()
            .Select((t, i) => new NavLink(t.Header as string ?? "", t.Tag as string ?? "", i))
            .ToList();

        NavA.ItemsSource = all.Where(n => GroupA.Contains(n.Header)).ToList();
        NavB.ItemsSource = all.Where(n => GroupB.Contains(n.Header)).ToList();
        NavC.ItemsSource = all.Where(n => !GroupA.Contains(n.Header) && !GroupB.Contains(n.Header)).ToList();

        if (Nav.SelectedIndex < 0) Nav.SelectedIndex = 0;   // страховка: контент не должен быть пустым
        SyncNavSelection();
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_navSync || sender is not ListBox list || list.SelectedItem is not NavLink link) return;

        _navSync = true;
        foreach (var other in NavLists) if (!ReferenceEquals(other, list)) other.SelectedIndex = -1;
        _navSync = false;

        Nav.SelectedIndex = link.Index;
    }

    /// <summary>Подсветить пункт, соответствующий открытой вкладке (переходы из Диагностики).</summary>
    private void SyncNavSelection()
    {
        if (_navSync) return;
        _navSync = true;
        foreach (var list in NavLists)
        {
            var items = list.ItemsSource as IEnumerable<NavLink>;
            var match = items?.FirstOrDefault(n => n.Index == Nav.SelectedIndex);
            list.SelectedItem = match;      // null снимет выделение в остальных группах
        }
        _navSync = false;
    }

    // --- Своя шапка окна ---
    private void Caption_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { Win_Maximize(sender, e); return; }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Win_Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Win_Maximize(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaxButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void Win_Close(object sender, RoutedEventArgs e) => Close();

    /// <summary>Значок в трее — неуправляемый ресурс, освобождаем явно.</summary>
    public void Dispose()
    {
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        _glassDelay?.Stop();
        GC.SuppressFinalize(this);
    }

    // --- Обновления (GitHub Releases) ---
    private UpdateInfo? _update;
    private string? _readyExe;      // скачанный файл, готовый к установке
    private bool _updateBusy;

    private async Task CheckUpdatesAsync()
    {
        UpdateChecker.CleanupOld();   // подчистить файл прошлой версии

        var cur = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        var upd = await UpdateChecker.Check(cur);
        if (upd is null) return;

        _update = upd;
        UpdateButton.Content = $"Обновить до v{upd.Latest.ToString(3)}";
        UpdateButton.Visibility = Visibility.Visible;

        // авто-режим: тихо качаем в фоне, ставим — по кнопке (чтобы не прерывать игру)
        if (Settings.AutoUpdate && upd.AssetUrl is not null)
            await DownloadUpdateAsync(silent: true);
    }

    private async Task<bool> DownloadUpdateAsync(bool silent)
    {
        if (_update is null || _updateBusy) return false;
        _updateBusy = true;
        var progress = new Progress<double>(p =>
            UpdateButton.Content = $"Загрузка {p:N0}%");
        try
        {
            _readyExe = await UpdateChecker.Download(_update, silent ? null : progress);
            if (_readyExe is null)
            {
                UpdateButton.Content = $"Обновить до v{_update.Latest.ToString(3)}";
                if (!silent) MessageBox.Show(this, "Не удалось скачать обновление. Попробуй позже или скачай вручную с GitHub.",
                                             "AntiLag", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            UpdateButton.Content = $"Установить v{_update.Latest.ToString(3)}";
            if (silent)
                _tray?.ShowBalloonTip(5000, "AntiLag",
                    $"Обновление v{_update.Latest.ToString(3)} загружено. Нажми «Установить» в программе.",
                    System.Windows.Forms.ToolTipIcon.Info);
            return true;
        }
        finally { _updateBusy = false; }
    }

    /// <summary>
    /// Одна кнопка делает всё: качает (если ещё не скачано) и сразу ставит
    /// с перезапуском. Само нажатие и есть подтверждение — лишних диалогов нет.
    /// </summary>
    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_update is null || _updateBusy) return;
        UpdateButton.IsEnabled = false;
        try
        {
            if (_readyExe is null && !await DownloadUpdateAsync(silent: false)) return;

            UpdateButton.Content = "Установка…";
            await Task.Delay(150);          // дать кнопке перерисоваться

            if (UpdateChecker.ApplyAndRestart(_readyExe!))
            {
                _exiting = true;
                if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
                System.Windows.Application.Current.Shutdown();
                return;
            }

            UpdateButton.Content = $"Установить v{_update.Latest.ToString(3)}";
            MessageBox.Show(this, "Не удалось заменить файл программы — возможно, он открыт из защищённой папки. " +
                                  "Сейчас откроется страница загрузки.",
                            "AntiLag", MessageBoxButton.OK, MessageBoxImage.Warning);
            try { Process.Start(new ProcessStartInfo(_update.PageUrl) { UseShellExecute = true }); } catch { }
        }
        finally { UpdateButton.IsEnabled = true; }
    }

    private void AutoUpdate_Checked(object sender, RoutedEventArgs e) { if (!_initializing) Settings.AutoUpdate = true; }
    private void AutoUpdate_Unchecked(object sender, RoutedEventArgs e) { if (!_initializing) Settings.AutoUpdate = false; }

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

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        // Окно уже один раз отрисовалось за экраном (поверхность корректна) —
        // теперь прячем в трей и возвращаем позицию на центр для будущих показов.
        if (_startHidden && !_hiddenOnce)
        {
            _hiddenOnce = true;
            Hide();
            ShowInTaskbar = false;
            Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
            Top = Math.Max(0, (SystemParameters.PrimaryScreenHeight - Height) / 2);
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
        _lookRows.Clear();
        foreach (var s in _engine.GetStatus())
        {
            var row = new TweakRow(s.Tweak, s.IsApplied);
            if (s.Tweak.Tier == TweakTier.Game) _gameRows.Add(row);
            else if (s.Tweak.Tier == TweakTier.Appearance) _lookRows.Add(row);
            else _rows.Add(row);
        }
        foreach (var r in AllRows)
            r.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TweakRow.IsSelected)) UpdatePending(); };

        BuildGameGroups();
        UpdatePending();
        UpdateLookStatus();
    }

    /// <summary>Сколько переключателей отличается от того, что сейчас в системе.</summary>
    private void UpdatePending()
    {
        UpdateLookStatus();
        int n = AllRows.Count(r => r.IsPending);
        PendingText.Text = n == 0 ? "" : $"{n} {Plural(n, "изменение", "изменения", "изменений")} не применено";
    }

    private static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        if (m10 == 1 && m100 != 11) return one;
        if (m10 is >= 2 and <= 4 && m100 is < 12 or > 14) return few;
        return many;
    }

    // --- Блоки раздела «Игровые» ---
    private string? _openGroup;   // null = показываем карточки блоков

    private void BuildGameGroups()
    {
        GameGroupsItems.ItemsSource = TweakCatalog.GameGroups
            .Select(g => new GroupCard(g, _gameRows.Where(r => r.GroupId == g.Id).ToList()))
            .Where(c => c.Total > 0)
            .ToList();
        ShowGameGroups();
    }

    private void ShowGameGroups()
    {
        _openGroup = null;
        GameGroupsItems.Visibility = Visibility.Visible;
        GameTweaksItems.Visibility = Visibility.Collapsed;
        GameBack.Visibility = Visibility.Collapsed;
        GameDescButton.Visibility = Visibility.Collapsed;
        GameSubtitle.Text = "Опциональные твики. Выбери блок — внутри только то, что относится к нему.";
    }

    private void GroupCard_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GroupCard card) return;
        _openGroup = card.Id;

        GameTweaksItems.ItemsSource = _gameRows.Where(r => r.GroupId == card.Id).ToList();
        GameGroupsItems.Visibility = Visibility.Collapsed;
        GameTweaksItems.Visibility = Visibility.Visible;
        GameBack.Visibility = Visibility.Visible;
        GameDescButton.Visibility = Visibility.Visible;
        GameSubtitle.Text = card.Description;
    }

    private void GroupBack_Click(object sender, RoutedEventArgs e) => ShowGameGroups();

    private void TweakExpand_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TweakRow row) row.IsExpanded = !row.IsExpanded;
    }

    /// <summary>Кнопка «Показать описания» — раскрывает или сворачивает все сразу.</summary>
    private void ToggleAllDesc_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        bool game = (b.Tag as string) == "game";

        var rows = game
            ? _gameRows.Where(r => _openGroup is null || r.GroupId == _openGroup).ToList()
            : _rows.ToList();
        if (rows.Count == 0) return;

        bool expand = rows.Any(r => !r.IsExpanded);   // если хоть одна свёрнута — раскрываем все
        foreach (var r in rows) r.IsExpanded = expand;
        b.Content = expand ? "Скрыть описания" : "Показать описания";
    }

    private void UpdateStatus()
    {
        // за удержанием следит сторож внутри сервиса — здесь только показываем
        double ms = _engine.Timer.CurrentMs;
        TimerText.Text = $"{ms:N4} ms";
        TimerState.Text = ms <= 0.6 ? "АКТИВЕН" : "ОБЫЧНЫЙ";
        TimerState.Foreground = ms <= 0.6 ? (Brush)Application.Current.Resources["Accent"] : Yellow;

        AdminText.Text = TweakEngine.IsElevated()
            ? $"admin: есть · автозапуск: {(AutoStartCheck.IsChecked == true ? "вкл" : "выкл")}"
            : "нет прав администратора";

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
            MonPeaks.Text    = _monitor.Peaks.ToString(System.Globalization.CultureInfo.CurrentCulture);
            MonStatus.Foreground = LevelColor(_monitor.StatusLevel);
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

    private static SolidColorBrush LevelPill(int level)
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
        TileProc.Text = _sysmon.Processes().ToString(System.Globalization.CultureInfo.CurrentCulture);
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
        SyncNavSelection();   // подсветка пункта в боковой панели
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

    private void HoldTimer_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;   // при старте таймер уже запущен в конструкторе
        _engine.Timer.Start();
    }

    private void HoldTimer_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;   // иначе разметка при загрузке снимала удержание
        _engine.Timer.Stop();
    }

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

    // --- Блоки раздела «Оформление» ---
    private void LookBlock_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string id) return;

        LookCards.Visibility = Visibility.Collapsed;
        LookBack.Visibility = Visibility.Visible;

        LookPanelCursors.Visibility  = id == "cursors"  ? Visibility.Visible : Visibility.Collapsed;
        LookPanelGlass.Visibility    = id == "glass"    ? Visibility.Visible : Visibility.Collapsed;
        LookPanelExplorer.Visibility = id == "explorer" ? Visibility.Visible : Visibility.Collapsed;

        LookSubtitle.Text = id switch
        {
            "cursors" => "Готовые схемы указателей — ставятся одним кликом. Свои паки тоже подхватываются.",
            "glass"   => "Стеклянный фон окон папок: размытие или заливка со своим цветом.",
            _         => "Панель навигации, расширения файлов, контекстное меню.",
        };
    }

    private void LookBack_Click(object sender, RoutedEventArgs e)
    {
        LookCards.Visibility = Visibility.Visible;
        LookBack.Visibility = Visibility.Collapsed;
        LookPanelCursors.Visibility = LookPanelGlass.Visibility = LookPanelExplorer.Visibility = Visibility.Collapsed;
        LookSubtitle.Text = "Внешний вид Windows: курсоры, стекло проводника и мелочи интерфейса.";
        UpdateLookStatus();
    }

    /// <summary>Счётчик включённых твиков вида на карточке блока.</summary>
    private void UpdateLookStatus()
    {
        if (LookExplorerStatus is null) return;
        LookExplorerStatus.Text = $"{_lookRows.Count(r => r.IsSelected)} / {_lookRows.Count} ВКЛ";
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!System.IO.File.Exists(Log.Path)) Log.Info("Журнал открыт пользователем — записей об ошибках нет.");
            Process.Start(new ProcessStartInfo(Log.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось открыть журнал: " + ex.Message, "AntiLag",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // --- Плотность строк ---
    private static void ApplyDensity(bool dense)
        => Application.Current.Resources["RowPad"] = dense ? new Thickness(17, 10, 17, 10) : new Thickness(17, 15, 17, 15);

    private void Dense_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        Settings.DenseRows = true; ApplyDensity(true);
    }

    private void Dense_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        Settings.DenseRows = false; ApplyDensity(false);
    }

    // --- Блоки раздела «Сеть» ---
    private void NetBlock_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string id) return;

        NetCards.Visibility = Visibility.Collapsed;
        NetBack.Visibility = Visibility.Visible;

        NetPanelState.Visibility = id == "state" ? Visibility.Visible : Visibility.Collapsed;
        NetPanelSpeed.Visibility = id == "speed" ? Visibility.Visible : Visibility.Collapsed;
        NetPanelPing.Visibility  = id == "ping"  ? Visibility.Visible : Visibility.Collapsed;

        NetSubtitle.Text = id switch
        {
            "state" => "Мониторинг задержки, потерь и джиттера в реальном времени.",
            "speed" => "Многопоточный замер: RU-сервер Selectel или Cloudflare.",
            _       => "Ping, TCPing, маршрут до узла и разбор типовых ошибок.",
        };
    }

    private void NetBack_Click(object sender, RoutedEventArgs e)
    {
        NetCards.Visibility = Visibility.Visible;
        NetBack.Visibility = Visibility.Collapsed;
        NetPanelState.Visibility = NetPanelSpeed.Visibility = NetPanelPing.Visibility = Visibility.Collapsed;
        NetSubtitle.Text = "Диагностика и измерения — по блокам.";
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
