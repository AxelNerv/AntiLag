using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AntiLagPro.Core;

public enum Severity { Ok, Warn, Bad, Info }

/// <summary>Одна находка диагностики для показа в UI.</summary>
public sealed record Finding(Severity Severity, string Title, string Detail, string? Fix = null, string? GoTo = null);

/// <summary>
/// Универсальный сканер: работает на ЛЮБОМ ПК, сам определяет железо и настройки.
/// Ничего не меняет — только читает. Тяжёлые проверки (NIC/аудио) идут через
/// powershell/powercfg, поэтому Scan() лучше вызывать в фоне (Task.Run).
/// </summary>
public sealed class DiagnosticsEngine
{
    public List<Finding> Scan()
    {
        var list = new List<Finding>();
        Safe(() => CheckTimer(list));
        Safe(() => CheckGlobalFlag(list));
        Safe(() => CheckPowerPlan(list));
        Safe(() => CheckPowerThrottling(list));
        Safe(() => CheckForegroundPriority(list));
        Safe(() => CheckUsbSuspend(list));
        Safe(() => CheckGameDvr(list));
        Safe(() => CheckMouseAccel(list));
        Safe(() => CheckFastStartup(list));
        Safe(() => CheckMemoryLoad(list));
        Safe(() => CheckDiskSpace(list));
        Safe(() => CheckPendingReboot(list));
        Safe(() => CheckNics(list));
        Safe(() => CheckAudio(list));
        return list;
    }

    private static void Safe(Action a) { try { a(); } catch { /* пропускаем сбойную проверку */ } }

    private static string Ps(string command)
    {
        // Заставляем powershell.exe выводить в UTF-8, иначе кириллица в именах
        // устройств приходит в Win-1251 и превращается в кракозябры.
        string full = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; " + command;
        var (_, outp, _) = ProcessRunner.Run("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{full}\"");
        return outp;
    }

    private static void CheckTimer(List<Finding> list)
    {
        var (cur, min, _) = Native.QueryResolutionMs();
        if (cur > 0.6)
            list.Add(new Finding(Severity.Warn, "Timer Resolution",
                $"Сейчас {cur:N4} ms (минимум возможный {min:N4} ms).",
                "Включить удержание 0.5 ms (тумблер вверху).", GoTo: "Базовое"));
        else
            list.Add(new Finding(Severity.Ok, "Timer Resolution", $"{cur:N4} ms — на минимуме.", null));
    }

    private static void CheckGlobalFlag(List<Finding> list)
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel");
        bool set = key?.GetValue("GlobalTimerResolutionRequests") is int v && v == 1;
        int build = Environment.OSVersion.Version.Build;

        if (build >= 22000 && !set)
            list.Add(new Finding(Severity.Bad, "Глобальный таймер (Windows 11)",
                "GlobalTimerResolutionRequests=0 — запрос 0.5 ms действует только внутри процесса. "
                + "Поэтому старые тулзы перестают помогать в играх.",
                "Применить твик «Глобальный Timer Resolution» (вкладка Базовое) + перезагрузка.", GoTo: "Базовое"));
        else if (set)
            list.Add(new Finding(Severity.Ok, "Глобальный таймер (Windows 11)",
                "Флаг включён — 0.5 ms действует на всю систему.", null));
    }

    private static void CheckPowerPlan(List<Finding> list)
    {
        string active = ProcessRunner.Powercfg("/getactivescheme");
        if (active.IndexOf("a1841308", StringComparison.OrdinalIgnoreCase) >= 0)
            list.Add(new Finding(Severity.Warn, "Схема питания",
                "Активна энергосберегающая схема — режет производительность.",
                "Применить «Схема питания AntiLag» (вкладка Базовое).", GoTo: "Базовое"));
        else
            list.Add(new Finding(Severity.Ok, "Схема питания", "Не энергосберегающая.", null));
    }

    private static void CheckUsbSuspend(List<Finding> list)
    {
        string outp = ProcessRunner.Powercfg(
            "/q SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226");
        // Ищем "...Setting Index: 0x00000001" (вкл) в строках про питание от сети.
        bool on = Regex.IsMatch(outp, @"(?:Setting Index|значени\w*)[^\r\n]*:\s*0x0*1\b", RegexOptions.IgnoreCase);
        if (on)
            list.Add(new Finding(Severity.Warn, "USB Selective Suspend",
                "Включён — USB-порты могут засыпать (микро-залипания мыши/клавы).",
                "Отключить в Параметрах питания (USB selective suspend)."));
        else
            list.Add(new Finding(Severity.Ok, "USB Selective Suspend", "Выключен (или не на сети).", null));
    }

    private static void CheckNics(List<Finding> list)
    {
        string outp = Ps(
            "Get-NetAdapter -Physical | ForEach-Object { $pm = $_ | Get-NetAdapterPowerManagement; "
            + "('{0}|{1}|{2}' -f $_.InterfaceDescription, $_.Status, $pm.AllowComputerToTurnOffDevice) }");

        foreach (var raw in outp.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            var p = line.Split('|');
            if (p.Length < 3) continue;
            string name = p[0], status = p[1], allow = p[2];

            if (allow.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                list.Add(new Finding(Severity.Warn, $"Сеть: {name}",
                    $"Статус: {status}. Windows разрешено усыплять адаптер ради экономии — частая причина DPC-спайков.",
                    "Запретить отключение для экономии (Игровые → «Отключить энергосбережение сети»).", GoTo: "Игровые"));
            else
                list.Add(new Finding(Severity.Ok, $"Сеть: {name}",
                    $"Статус: {status}. Питание: отключение запрещено.", null));
        }
    }

    private static void CheckAudio(List<Finding> list)
    {
        string outp = Ps("Get-CimInstance Win32_SoundDevice | Where-Object {$_.Status -eq 'OK'} "
            + "| ForEach-Object { $_.Name }");
        var names = outp.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (names.Count == 0) return;

        list.Add(new Finding(Severity.Info, $"Аудио-устройства: {names.Count}",
            string.Join(", ", names),
            "USB-аудио и виртуальные устройства — частый источник DPC. Лишние можно отключить в Диспетчере устройств."));
    }

    private static void CheckPowerThrottling(List<Finding> list)
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling");
        bool off = k?.GetValue("PowerThrottlingOff") is int v && v == 1;
        if (!off)
            list.Add(new Finding(Severity.Warn, "Power Throttling",
                "Включён — Windows может придушивать приложения ради экономии.",
                "Отключить Power Throttling.", GoTo: "Базовое"));
        else
            list.Add(new Finding(Severity.Ok, "Power Throttling", "Отключён.", null));
    }

    private static void CheckForegroundPriority(List<Finding> list)
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
        int v = k?.GetValue("Win32PrioritySeparation") as int? ?? 2;
        if (v != 0x26)
            list.Add(new Finding(Severity.Warn, "Приоритет переднего плана",
                $"Win32PrioritySeparation={v} — не оптимально для игр.",
                "Применить «Приоритет переднего плана».", GoTo: "Игровые"));
        else
            list.Add(new Finding(Severity.Ok, "Приоритет переднего плана", "Оптимально для игр.", null));
    }

    private static void CheckGameDvr(List<Finding> list)
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
        bool off = k?.GetValue("GameDVR_Enabled") is int v && v == 0;
        if (!off)
            list.Add(new Finding(Severity.Warn, "Game Bar (DVR)",
                "Фоновая запись включена — тратит ресурсы и FPS.",
                "Отключить фоновую запись Game Bar.", GoTo: "Игровые"));
        else
            list.Add(new Finding(Severity.Ok, "Game Bar (DVR)", "Фоновая запись отключена.", null));
    }

    private static void CheckMouseAccel(List<Finding> list)
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
        bool on = (k?.GetValue("MouseSpeed") as string) != "0";
        if (on)
            list.Add(new Finding(Severity.Info, "Акселерация мыши",
                "Включена — курсор двигается не 1:1 (хуже для шутеров).",
                "Уменьшить задержку ввода (мышь).", GoTo: "Игровые"));
        else
            list.Add(new Finding(Severity.Ok, "Акселерация мыши", "Отключена (курсор 1:1).", null));
    }

    private static void CheckFastStartup(List<Finding> list)
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
        bool on = k?.GetValue("HiberbootEnabled") is int v && v == 1;
        if (on)
            list.Add(new Finding(Severity.Info, "Быстрый запуск",
                "Включён — иногда мешает корректной загрузке драйверов и обновлений.",
                "Можно отключить в Параметрах питания (по желанию)."));
        else
            list.Add(new Finding(Severity.Ok, "Быстрый запуск", "Отключён.", null));
    }

    private static void CheckMemoryLoad(List<Finding> list)
    {
        var m = MemoryCleaner.GetStatus();
        if (m.loadPercent > 85)
            list.Add(new Finding(Severity.Warn, "Оперативная память",
                $"Загрузка ОЗУ {m.loadPercent}% — мало свободной памяти.",
                "Очистить память.", GoTo: "Ускорение"));
        else
            list.Add(new Finding(Severity.Ok, "Оперативная память", $"Загрузка ОЗУ {m.loadPercent}%.", null));
    }

    private static void CheckDiskSpace(List<Finding> list)
    {
        string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var d = new DriveInfo(root);
        if (!d.IsReady) return;
        double freeGb = d.AvailableFreeSpace / 1073741824.0;
        double pct = d.TotalSize > 0 ? d.AvailableFreeSpace * 100.0 / d.TotalSize : 100;
        if (pct < 10)
            list.Add(new Finding(Severity.Bad, $"Системный диск {d.Name}",
                $"Мало места: {freeGb:N0} ГБ свободно ({pct:N0}%). Это тормозит систему.",
                "Освободить место на диске."));
        else if (pct < 15)
            list.Add(new Finding(Severity.Warn, $"Системный диск {d.Name}",
                $"Заполняется: {freeGb:N0} ГБ свободно ({pct:N0}%).", "Желательно освободить место."));
        else
            list.Add(new Finding(Severity.Ok, $"Системный диск {d.Name}", $"Свободно {freeGb:N0} ГБ ({pct:N0}%).", null));
    }

    private static void CheckPendingReboot(List<Finding> list)
    {
        bool KeyExists(string p) { using var k = Registry.LocalMachine.OpenSubKey(p); return k is not null; }
        bool pending =
            KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") ||
            KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
        if (pending)
            list.Add(new Finding(Severity.Warn, "Перезагрузка",
                "Ожидается перезагрузка — применятся обновления/настройки.", "Перезагрузить ПК."));
    }
}
