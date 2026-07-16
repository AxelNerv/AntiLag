using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Аппаратное планирование GPU (HAGS, HwSchMode=2): видеокарта сама управляет
/// своей очередью команд вместо CPU — ниже накладные расходы и инпут-лаг на
/// современных GPU (RTX 2000+/RX 5000+). На старых картах или при 8 ГБ VRAM
/// впритык может давать статтеры — потому опциональный тумблер.
/// </summary>
public sealed class HagsTweak : ITweak
{
    public string Id => "hags";
    public string Name => "Аппаратное планирование GPU (HAGS)";
    public string Description =>
        "Видеокарта сама планирует свою очередь команд вместо процессора — ниже " +
        "нагрузка на CPU и инпут-лаг. Рекомендуется для современных GPU (RTX 2000+ / " +
        "RX 5000+); на старых или с малым объёмом видеопамяти возможны статтеры. " +
        "Вступает в силу после перезагрузки.";
    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => true;

    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

    public bool IsApplied()
    {
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath);
        return k?.GetValue("HwSchMode") is int v && v == 2;
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true)
            ?? Registry.LocalMachine.CreateSubKey(KeyPath);
        slot["mode"] = (k.GetValue("HwSchMode") as int?)?.ToString();
        k.SetValue("HwSchMode", 2, RegistryValueKind.DWord);
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
        if (k is not null)
        {
            if (int.TryParse(slot.GetValueOrDefault("mode"), out int v))
                k.SetValue("HwSchMode", v, RegistryValueKind.DWord);
            else
                k.DeleteValue("HwSchMode", throwOnMissingValue: false);
        }
        backup.Remove(Id);
    }
}
