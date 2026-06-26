using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Уменьшить задержку ввода: отключает акселерацию мыши (Enhance pointer precision).
/// Курсор двигается 1:1 — точнее в шутерах. Эффект после перезахода в систему.
/// </summary>
public sealed class InputLagTweak : ITweak
{
    public string Id => "input-lag-mouse";
    public string Name => "Уменьшить задержку ввода (мышь)";
    public string Description =>
        "Отключает акселерацию мыши — движение курсора 1:1, ровнее прицел. " +
        "Эффект после перезахода. Не рекомендуется, если привык к ускорению курсора.";
    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => false;

    private const string KeyPath = @"Control Panel\Mouse";

    public bool IsApplied()
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
        return (k?.GetValue("MouseSpeed") as string) == "0";
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(KeyPath);
        slot["s"]  = k.GetValue("MouseSpeed") as string;
        slot["t1"] = k.GetValue("MouseThreshold1") as string;
        slot["t2"] = k.GetValue("MouseThreshold2") as string;
        k.SetValue("MouseSpeed", "0", RegistryValueKind.String);
        k.SetValue("MouseThreshold1", "0", RegistryValueKind.String);
        k.SetValue("MouseThreshold2", "0", RegistryValueKind.String);
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        if (k is not null)
        {
            k.SetValue("MouseSpeed", slot.GetValueOrDefault("s") ?? "1", RegistryValueKind.String);
            k.SetValue("MouseThreshold1", slot.GetValueOrDefault("t1") ?? "6", RegistryValueKind.String);
            k.SetValue("MouseThreshold2", slot.GetValueOrDefault("t2") ?? "10", RegistryValueKind.String);
        }
        backup.Remove(Id);
    }
}
