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

    /// <summary>Скачивать обновления автоматически (по умолчанию ДА).</summary>
    public static bool AutoUpdate
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
            return !(k?.GetValue("AutoUpdate") is int v && v == 0);
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(KeyPath);
            k.SetValue("AutoUpdate", value ? 1 : 0, RegistryValueKind.DWord);
        }
    }

    /// <summary>Прозрачный фон окон проводника (по умолчанию ВЫКЛ).</summary>
    public static bool Glass
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
            return k?.GetValue("Glass") is int v && v == 1;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(KeyPath);
            k.SetValue("Glass", value ? 1 : 0, RegistryValueKind.DWord);
        }
    }

    /// <summary>Вид эффекта: 2 — слюда, 3 — акрил, 4 — вкладки, 10 — размытие, 11 — заливка.</summary>
    public static int GlassKind
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
            return k?.GetValue("GlassKind") is int v && (v is >= 2 and <= 4 || v is 10 or 11) ? v : 2;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(KeyPath);
            k.SetValue("GlassKind", value, RegistryValueKind.DWord);
        }
    }

    /// <summary>Свой цвет стекла в виде #RRGGBB.</summary>
    public static string GlassColor
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
            return k?.GetValue("GlassColor") as string is { Length: > 0 } s ? s : "#202026";
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(KeyPath);
            k.SetValue("GlassColor", value, RegistryValueKind.String);
        }
    }

    /// <summary>Плотность заливки 0..255 (чем меньше — тем прозрачнее).</summary>
    public static int GlassAlpha
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
            return k?.GetValue("GlassAlpha") is int v && v is >= 0 and <= 255 ? v : 160;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(KeyPath);
            k.SetValue("GlassAlpha", Math.Clamp(value, 0, 255), RegistryValueKind.DWord);
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
