using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Минимальная задержка сетевого адаптера: отключает Interrupt Moderation
/// (драйвер копит пакеты пачками → джиттер) и Large Send Offload (LSO,
/// замечен в микро-задержках). Меняются стандартизованные ключи драйвера
/// (*InterruptModeration, *LsoV2IPv4/6) только у ФИЗИЧЕСКИХ адаптеров,
/// затем адаптер перезапускается (интернет моргнёт на пару секунд).
/// </summary>
public sealed class NicLatencyTweak : ITweak
{
    public string Id => "nic-latency";
    public string Name => "Сетевой адаптер: минимум задержки";
    public string Description =>
        "Отключает Interrupt Moderation (батчинг пакетов → джиттер) и Large Send Offload " +
        "на сетевой карте — ровнее пинг в онлайне. При применении интернет моргнёт на " +
        "пару секунд (адаптер перезапустится). Чуть выше нагрузка на CPU при загрузках.";
    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => false;

    private const string ClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
    private static readonly string[] Keywords = { "*InterruptModeration", "*LsoV2IPv4", "*LsoV2IPv6" };

    private static IEnumerable<string> NicSubkeys(RegistryKey root)
        => root.GetSubKeyNames().Where(n => Regex.IsMatch(n, @"^\d{4}$"));

    private static bool IsPhysical(RegistryKey? k)
        => k?.GetValue("NetCfgInstanceId") is not null
           && k.GetValue("Characteristics") is int c && (c & 0x4) != 0;

    public bool IsApplied()
    {
        using var root = Registry.LocalMachine.OpenSubKey(ClassKey);
        if (root is null) return false;
        bool any = false;
        foreach (var sub in NicSubkeys(root))
        {
            using var k = root.OpenSubKey(sub);
            if (!IsPhysical(k)) continue;
            foreach (var kw in Keywords)
            {
                // проверяем только ключи, которые драйвер реально поддерживает
                if (k!.GetValue(kw) is string v)
                {
                    any = true;
                    if (v != "0") return false;
                }
            }
        }
        return any;
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using (var root = Registry.LocalMachine.OpenSubKey(ClassKey, writable: true))
        {
            if (root is null) return;
            foreach (var sub in NicSubkeys(root))
            {
                using var k = root.OpenSubKey(sub, writable: true);
                if (!IsPhysical(k)) continue;
                foreach (var kw in Keywords)
                {
                    if (k!.GetValue(kw) is string v)   // меняем только существующие
                    {
                        slot[$"{sub}|{kw}"] = v;
                        k.SetValue(kw, "0", RegistryValueKind.String);
                    }
                }
            }
        }
        RestartPhysicalAdapters();
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        using (var root = Registry.LocalMachine.OpenSubKey(ClassKey, writable: true))
        {
            if (root is not null)
            {
                foreach (var kv in slot)
                {
                    var parts = kv.Key.Split('|');
                    if (parts.Length != 2) continue;
                    using var k = root.OpenSubKey(parts[0], writable: true);
                    k?.SetValue(parts[1], kv.Value ?? "1", RegistryValueKind.String);
                }
            }
        }
        backup.Remove(Id);
        RestartPhysicalAdapters();
    }

    /// Перезапуск физических адаптеров, чтобы драйвер перечитал настройки.
    private static void RestartPhysicalAdapters()
    {
        try
        {
            ProcessRunner.Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command " +
                "\"Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | Restart-NetAdapter -Confirm:$false\"");
        }
        catch { /* если не вышло — настройки применятся после перезагрузки */ }
    }
}
