using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Сократить задержку клавиатуры: KeyboardDelay=0 (мин. пауза до автоповтора),
/// KeyboardSpeed=31 (макс. частота повтора). Применяется после перезахода/перезагрузки.
/// </summary>
public sealed class KeyboardSpeedTweak : ITweak
{
    public string Id => "keyboard-speed";
    public string Name => "Сократить задержку клавиатуры";
    public string Description =>
        "Минимальная пауза и максимальная частота автоповтора клавиш. " +
        "Эффект после перезахода в систему. Не рекомендуется, если привык к обычному повтору.";
    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => false;

    private const string KeyPath = @"Control Panel\Keyboard";

    public bool IsApplied()
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
        return (k?.GetValue("KeyboardDelay") as string) == "0"
            && (k?.GetValue("KeyboardSpeed") as string) == "31";
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(KeyPath);

        slot["delay"] = k.GetValue("KeyboardDelay") as string;
        slot["speed"] = k.GetValue("KeyboardSpeed") as string;

        k.SetValue("KeyboardDelay", "0", RegistryValueKind.String);
        k.SetValue("KeyboardSpeed", "31", RegistryValueKind.String);
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        if (k is not null)
        {
            k.SetValue("KeyboardDelay", slot.GetValueOrDefault("delay") ?? "1", RegistryValueKind.String);
            k.SetValue("KeyboardSpeed", slot.GetValueOrDefault("speed") ?? "31", RegistryValueKind.String);
        }
        backup.Remove(Id);
    }
}
