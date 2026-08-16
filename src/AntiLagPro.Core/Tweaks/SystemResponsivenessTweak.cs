using System.Globalization;
using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Игровой приоритет: SystemResponsiveness=10 — система резервирует меньше
/// времени под фоновые задачи. Сетевой троттлинг вынесен в отдельный твик
/// (NetworkThrottlingTweak), потому что относится к сети, а не к планировщику.
/// </summary>
public sealed class SystemResponsivenessTweak : ITweak
{
    public string Id => "system-responsiveness";
    public string Name => "Игровой приоритет";
    public string Description =>
        "SystemResponsiveness = 10: система резервирует меньше ресурсов под фоновые задачи, " +
        "больше достаётся активной игре.";
    public TweakTier Tier => TweakTier.Universal;
    public bool RequiresReboot => false;

    internal const string KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    public bool IsApplied()
    {
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath);
        return k?.GetValue("SystemResponsiveness") is int v && v == 10;
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true)
            ?? Registry.LocalMachine.CreateSubKey(KeyPath);
        slot["sr"] = (k.GetValue("SystemResponsiveness") as int?)?.ToString(CultureInfo.InvariantCulture);
        k.SetValue("SystemResponsiveness", 10, RegistryValueKind.DWord);
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
        if (k is not null)
        {
            if (int.TryParse(slot.GetValueOrDefault("sr"), out int sr)) k.SetValue("SystemResponsiveness", sr, RegistryValueKind.DWord);
            else k.SetValue("SystemResponsiveness", 20, RegistryValueKind.DWord);

            // старые версии хранили сетевой троттлинг в этом же слоте — вернём и его
            if (slot.ContainsKey("nt"))
            {
                if (int.TryParse(slot.GetValueOrDefault("nt"), out int nt)) k.SetValue("NetworkThrottlingIndex", nt, RegistryValueKind.DWord);
                else k.SetValue("NetworkThrottlingIndex", 10, RegistryValueKind.DWord);
            }
        }
        backup.Remove(Id);
    }
}

/// <summary>
/// Отключение сетевого троттлинга (NetworkThrottlingIndex = 0xFFFFFFFF).
/// Windows по умолчанию ограничивает обработку сетевых пакетов ради
/// мультимедиа — для игр это лишняя задержка.
/// </summary>
public sealed class NetworkThrottlingTweak : ITweak
{
    public string Id => "network-throttling";
    public string Name => "Отключить сетевой троттлинг";
    public string Description =>
        "Windows ограничивает обработку сетевых пакетов (около 10 тысяч в секунду), чтобы не мешать " +
        "мультимедиа. В играх это лишняя задержка — снимаем ограничение.";
    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => false;

    public bool IsApplied()
    {
        using var k = Registry.LocalMachine.OpenSubKey(SystemResponsivenessTweak.KeyPath);
        return k?.GetValue("NetworkThrottlingIndex") is int v && v == unchecked((int)0xFFFFFFFF);
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using var k = Registry.LocalMachine.OpenSubKey(SystemResponsivenessTweak.KeyPath, writable: true)
            ?? Registry.LocalMachine.CreateSubKey(SystemResponsivenessTweak.KeyPath);
        slot["nt"] = (k.GetValue("NetworkThrottlingIndex") as int?)?.ToString(CultureInfo.InvariantCulture);
        unchecked { k.SetValue("NetworkThrottlingIndex", (int)0xFFFFFFFF, RegistryValueKind.DWord); }
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        using var k = Registry.LocalMachine.OpenSubKey(SystemResponsivenessTweak.KeyPath, writable: true);
        if (k is not null)
        {
            if (int.TryParse(slot.GetValueOrDefault("nt"), out int nt)) k.SetValue("NetworkThrottlingIndex", nt, RegistryValueKind.DWord);
            else k.SetValue("NetworkThrottlingIndex", 10, RegistryValueKind.DWord);
        }
        backup.Remove(Id);
    }
}
