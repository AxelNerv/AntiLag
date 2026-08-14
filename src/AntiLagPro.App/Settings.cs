using Microsoft.Win32;

namespace AntiLagPro.App;

/// <summary>Простые настройки приложения в реестре (HKCU\Software\AntiLag).</summary>
internal static class Settings
{
    private const string KeyPath = @"Software\AntiLag";

    /// <summary>Сворачивать в трей при закрытии (по умолчанию ДА).</summary>
    public static bool MinimizeToTray
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
            return !(k?.GetValue("MinimizeToTray") is int v && v == 0); // нет значения = true
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(KeyPath);
            k.SetValue("MinimizeToTray", value ? 1 : 0, RegistryValueKind.DWord);
        }
    }

    /// <summary>Акцентный цвет в виде #RRGGBB (пусто = цвет по умолчанию).</summary>
    public static string? AccentHex
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
            return k?.GetValue("AccentHex") as string;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(KeyPath);
            k.SetValue("AccentHex", value ?? "", RegistryValueKind.String);
        }
    }
}
