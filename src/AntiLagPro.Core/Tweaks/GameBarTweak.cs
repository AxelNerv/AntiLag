using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Отключить фоновую запись Xbox Game Bar (Game DVR) — освобождает ресурсы и FPS.
/// Правит GameDVR_Enabled (HKCU) + политику AllowGameDVR (HKLM).
/// </summary>
public sealed class GameBarTweak : ITweak
{
    public string Id => "gamebar-dvr";
    public string Name => "Отключить фоновую запись Game Bar (DVR)";
    public string Description =>
        "Отключает автозапись/снимки Xbox Game Bar. Освобождает ресурсы, чуть выше FPS. " +
        "Не рекомендуется, если ты записываешь геймплей через Game Bar.";
    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => false;

    private const string UserKey   = @"System\GameConfigStore";
    private const string PolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\GameDVR";

    public bool IsApplied()
    {
        using var k = Registry.CurrentUser.OpenSubKey(UserKey);
        return k?.GetValue("GameDVR_Enabled") is int v && v == 0;
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);

        using (var k = Registry.CurrentUser.CreateSubKey(UserKey))
        {
            slot["dvr"] = (k.GetValue("GameDVR_Enabled") as int?)?.ToString();
            k.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
        }
        using (var p = Registry.LocalMachine.CreateSubKey(PolicyKey))
        {
            slot["policyExisted"] = (p.GetValue("AllowGameDVR") is not null).ToString();
            slot["policy"] = (p.GetValue("AllowGameDVR") as int?)?.ToString();
            p.SetValue("AllowGameDVR", 0, RegistryValueKind.DWord);
        }
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);

        using (var k = Registry.CurrentUser.OpenSubKey(UserKey, writable: true))
        {
            if (k is not null)
            {
                if (int.TryParse(slot.GetValueOrDefault("dvr"), out int v))
                    k.SetValue("GameDVR_Enabled", v, RegistryValueKind.DWord);
                else
                    k.SetValue("GameDVR_Enabled", 1, RegistryValueKind.DWord); // дефолт = вкл
            }
        }
        using (var p = Registry.LocalMachine.OpenSubKey(PolicyKey, writable: true))
        {
            if (p is not null)
            {
                bool existed = slot.GetValueOrDefault("policyExisted") == "True";
                if (existed && int.TryParse(slot.GetValueOrDefault("policy"), out int pv))
                    p.SetValue("AllowGameDVR", pv, RegistryValueKind.DWord);
                else
                    p.DeleteValue("AllowGameDVR", throwOnMissingValue: false);
            }
        }
        backup.Remove(Id);
    }
}
