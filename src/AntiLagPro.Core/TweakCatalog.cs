namespace AntiLagPro.Core;

/// <summary>Блок твиков внутри раздела (карточка на экране раздела).</summary>
public sealed record TweakGroup(string Id, string Name, string Description);

/// <summary>
/// Витрина твика: в какой блок попадает, что меняет и о чём предупредить.
/// Держим отдельно от твиков, чтобы менять раскладку, не трогая логику применения.
/// </summary>
public sealed record TweakInfo(string GroupId, string Target, string? Tag = null, string? Warning = null);

/// <summary>
/// Раскладка твиков по блокам. Ключи реестра берутся ИЗ КОДА твиков, а не из
/// макета — в интерфейсе должно быть то, что программа действительно делает.
/// </summary>
public static class TweakCatalog
{
    // ---- блоки по разделам ----
    public static readonly TweakGroup[] GameGroups =
    {
        new("input", "Ввод и отклик",      "Мышь, клавиатура, очередь ввода — всё между рукой и игрой."),
        new("bg",    "Фон и службы",       "Фоновая работа Windows, которая ест кадры и диск."),
        new("cpu",   "Процессор и таймеры","Как система делит процессорное время и насколько ровно идёт таймер."),
        new("gpu",   "Графика",            "Планирование GPU и вывод кадров."),
        new("gnet",  "Сеть в играх",       "Nagle, троттлинг, энергосбережение адаптера и прерывания."),
    };

    public static readonly TweakGroup[] LookGroups =
    {
        new("explorer", "Проводник и меню", "Панель навигации, расширения файлов, контекстное меню."),
    };

    // ---- метаданные твиков ----
    private static readonly Dictionary<string, TweakInfo> Map = new()
    {
        // Базовое (плоский список, GroupId не используется)
        ["global-timer-resolution"] = new("", @"HKLM\SYSTEM\...\kernel\GlobalTimerResolutionRequests", "перезагрузка"),
        ["power-plan"]              = new("", "powercfg /duplicatescheme"),
        ["core-parking"]            = new("", "powercfg CPMINCORES = 100", null,
                                          "Схема питания AntiLag уже отключает парковку — вместе с ней твик избыточен"),
        ["system-responsiveness"]   = new("", @"HKLM\...\Multimedia\SystemProfile\SystemResponsiveness"),
        ["power-throttling"]        = new("", @"HKLM\SYSTEM\...\Power\PowerThrottling", "ноутбук",
                                          "Снижает время автономной работы ноутбука"),

        // Игровые — ввод и отклик
        ["input-lag-mouse"] = new("input", @"HKCU\Control Panel\Mouse\MouseSpeed", null,
                                  "Непривычно, если привык к ускорению курсора"),
        ["keyboard-speed"]  = new("input", @"HKCU\Control Panel\Keyboard"),
        ["input-queue"]     = new("input", @"HKLM\SYSTEM\...\mouclass|kbdclass\DataQueueSize", "перезагрузка"),

        // Игровые — фон и службы
        ["gamebar-dvr"] = new("bg", @"HKCU\...\GameDVR\AppCaptureEnabled", null,
                              "Не включай, если пишешь геймплей через Game Bar"),
        ["svc-sysmain"] = new("bg", "служба SysMain", null, "Не рекомендуется на обычном HDD"),
        ["svc-wsearch"] = new("bg", "служба WSearch", null, "Поиск по файлам станет медленнее"),
        ["game-mode"]   = new("bg", @"HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled"),

        // Игровые — процессор и таймеры
        ["foreground-priority"] = new("cpu", @"HKLM\SYSTEM\...\PriorityControl\Win32PrioritySeparation", null,
                                      "Если появятся микро-фризы — откати"),
        ["mmcss-games"]         = new("cpu", @"HKLM\...\SystemProfile\Tasks\Games"),
        ["dynamic-tick"]        = new("cpu", "bcdedit /set disabledynamictick yes", "перезагрузка"),

        // Игровые — графика
        ["hags"] = new("gpu", @"HKLM\SYSTEM\...\GraphicsDrivers\HwSchMode", "перезагрузка",
                       "Эффект зависит от связки видеокарты и драйвера — замерь до и после"),

        // Игровые — сеть
        ["nagle"]              = new("gnet", @"HKLM\SYSTEM\...\Tcpip\Parameters\Interfaces"),
        ["network-throttling"] = new("gnet", @"HKLM\...\SystemProfile\NetworkThrottlingIndex"),
        ["nic-power"]          = new("gnet", @"HKLM\SYSTEM\...\Class\{4d36e972}\PnPCapabilities"),
        ["nic-latency"]        = new("gnet", @"NDIS: *InterruptModeration, *LsoV2IPv4/6", null,
                                     "Интернет моргнёт на пару секунд — адаптер перезапустится"),

        // Оформление
        ["explorer-gallery"]     = new("explorer", @"HKCU\...\CLSID\{e88865ea}\System.IsPinnedToNameSpaceTree"),
        ["explorer-home"]        = new("explorer", @"HKLM\SOFTWARE\...\Explorer\HubMode"),
        ["file-extensions"]      = new("explorer", @"HKCU\...\Explorer\Advanced\HideFileExt"),
        ["classic-context-menu"] = new("explorer", @"HKCU\...\CLSID\{86ca1aa0}\InprocServer32"),
    };

    public static TweakInfo For(string tweakId) =>
        Map.TryGetValue(tweakId, out var i) ? i : new TweakInfo("", "");

    /// <summary>Бейдж у твика: из каталога, иначе — по требованию перезагрузки.</summary>
    public static string? TagFor(ITweak t) => For(t.Id).Tag ?? (t.RequiresReboot ? "перезагрузка" : null);
}
