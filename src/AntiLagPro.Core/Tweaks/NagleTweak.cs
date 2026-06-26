using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Отключить алгоритм Нейгла на активном сетевом адаптере: TcpAckFrequency=1,
/// TCPNoDelay=1. Мелкие пакеты отправляются сразу — ниже задержка в онлайн-играх.
/// Применяется только к активному адаптеру, полностью обратимо.
/// </summary>
public sealed class NagleTweak : ITweak
{
    public string Id => "nagle";
    public string Name => "Отключить Nagle (онлайн-игры)";
    public string Description =>
        "TcpAckFrequency=1 и TCPNoDelay=1 на активном адаптере — мгновенная отправка мелких пакетов, " +
        "ниже задержка в сетевых играх. Если интернет станет хуже — откати.";
    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => false;

    private static string? Iface => NetworkTools.GetActiveInterfaceId();
    private static string Path(string id) =>
        $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{id}";

    public bool IsApplied()
    {
        var id = Iface;
        if (id is null) return false;
        using var k = Registry.LocalMachine.OpenSubKey(Path(id));
        return k?.GetValue("TcpAckFrequency") is int v && v == 1;
    }

    public void Apply(BackupData backup)
    {
        var id = Iface ?? throw new InvalidOperationException("Активный сетевой адаптер не найден.");
        var slot = backup.For(Id);
        slot["iface"] = id;
        using var k = Registry.LocalMachine.OpenSubKey(Path(id), writable: true)
            ?? throw new InvalidOperationException("Ключ адаптера не найден.");
        slot["ack"] = (k.GetValue("TcpAckFrequency") as int?)?.ToString() ?? "none";
        slot["nodelay"] = (k.GetValue("TCPNoDelay") as int?)?.ToString() ?? "none";
        k.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
        k.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        string? id = slot.GetValueOrDefault("iface");
        if (id is not null)
        {
            using var k = Registry.LocalMachine.OpenSubKey(Path(id), writable: true);
            if (k is not null)
            {
                RestoreOne(k, "TcpAckFrequency", slot.GetValueOrDefault("ack"));
                RestoreOne(k, "TCPNoDelay", slot.GetValueOrDefault("nodelay"));
            }
        }
        backup.Remove(Id);
    }

    private static void RestoreOne(RegistryKey k, string name, string? old)
    {
        if (old is null || old == "none") k.DeleteValue(name, throwOnMissingValue: false);
        else if (int.TryParse(old, out int v)) k.SetValue(name, v, RegistryValueKind.DWord);
    }
}
