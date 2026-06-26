using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Отключить Power Throttling: PowerThrottlingOff=1.
/// Windows перестаёт "душить" приложения ради энергосбережения — игры и фон
/// работают на полную мощность. Полностью обратимо.
/// </summary>
public sealed class PowerThrottlingTweak : ITweak
{
    public string Id => "power-throttling";
    public string Name => "Отключить Power Throttling";
    public string Description =>
        "Запрещает Windows троттлить (придушивать) приложения ради экономии энергии. " +
        "На десктопе полезно; на ноутбуке снижает время автономной работы.";
    public TweakTier Tier => TweakTier.Universal;
    public bool RequiresReboot => false;

    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
    private const string ValueName = "PowerThrottlingOff";

    public bool IsApplied()
    {
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath);
        return k?.GetValue(ValueName) is int v && v == 1;
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using var k = Registry.LocalMachine.CreateSubKey(KeyPath);
        object? old = k.GetValue(ValueName);
        slot["existed"] = (old is not null).ToString();
        slot["v"] = (old as int?)?.ToString();
        k.SetValue(ValueName, 1, RegistryValueKind.DWord);
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
        if (k is not null)
        {
            bool existed = slot.GetValueOrDefault("existed") == "True";
            if (existed && int.TryParse(slot.GetValueOrDefault("v"), out int v))
                k.SetValue(ValueName, v, RegistryValueKind.DWord);
            else
                k.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        backup.Remove(Id);
    }
}
