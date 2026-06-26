using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Приоритет переднего плана: Win32PrioritySeparation = 0x26.
/// Активное окно (игра) получает больше процессорного времени, фон — меньше.
/// </summary>
public sealed class ForegroundPriorityTweak : ITweak
{
    public string Id => "foreground-priority";
    public string Name => "Приоритет переднего плана (меньше фону)";
    public string Description =>
        "Win32PrioritySeparation=0x26 — больше CPU активному окну (игре), меньше фоновым процессам. " +
        "Если появятся микро-фризы — откати.";
    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => false;

    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string ValueName = "Win32PrioritySeparation";

    public bool IsApplied()
    {
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath);
        return k?.GetValue(ValueName) is int v && v == 0x26;
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true)
            ?? Registry.LocalMachine.CreateSubKey(KeyPath);
        slot["v"] = (k.GetValue(ValueName) as int?)?.ToString();
        k.SetValue(ValueName, 0x26, RegistryValueKind.DWord);
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
        if (k is not null)
        {
            if (int.TryParse(slot.GetValueOrDefault("v"), out int v)) k.SetValue(ValueName, v, RegistryValueKind.DWord);
            else k.SetValue(ValueName, 2, RegistryValueKind.DWord); // дефолт Windows
        }
        backup.Remove(Id);
    }
}
