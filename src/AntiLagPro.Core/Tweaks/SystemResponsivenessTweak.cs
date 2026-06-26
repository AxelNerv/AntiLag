using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Игровой приоритет: SystemResponsiveness=10 (меньше резерва под фон) и
/// NetworkThrottlingIndex=0xFFFFFFFF (отключить сетевой throttling — ниже пинг).
/// Обе настройки в SystemProfile, полностью обратимы.
/// </summary>
public sealed class SystemResponsivenessTweak : ITweak
{
    public string Id => "system-responsiveness";
    public string Name => "Игровой приоритет (multimedia + сеть)";
    public string Description =>
        "SystemResponsiveness=10 и отключение network throttling — система резервирует меньше под фоновые задачи, " +
        "ниже задержка и пинг в играх.";
    public TweakTier Tier => TweakTier.Universal;
    public bool RequiresReboot => false;

    private const string KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

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
        slot["sr"] = (k.GetValue("SystemResponsiveness") as int?)?.ToString();
        slot["nt"] = (k.GetValue("NetworkThrottlingIndex") as int?)?.ToString();
        k.SetValue("SystemResponsiveness", 10, RegistryValueKind.DWord);
        unchecked { k.SetValue("NetworkThrottlingIndex", (int)0xFFFFFFFF, RegistryValueKind.DWord); }
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

            if (int.TryParse(slot.GetValueOrDefault("nt"), out int nt)) k.SetValue("NetworkThrottlingIndex", nt, RegistryValueKind.DWord);
            else k.SetValue("NetworkThrottlingIndex", 10, RegistryValueKind.DWord);
        }
        backup.Remove(Id);
    }
}
