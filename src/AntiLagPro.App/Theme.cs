using System.Windows;
using System.Windows.Media;

namespace AntiLagPro.App;

/// <summary>
/// Акцентный цвет приложения. Кисти лежат в ресурсах App (Accent / AccentFg),
/// в разметке используются через DynamicResource — поэтому смена цвета
/// применяется мгновенно, без перезапуска.
/// </summary>
internal static class Theme
{
    public static readonly (string Name, Color Color)[] Presets =
    {
        ("Графит", Color.FromRgb(0xD4, 0xD4, 0xD8)),   // нейтральный, по умолчанию
        ("Сталь",  Color.FromRgb(0x8A, 0xB0, 0xEA)),   // спокойный голубой
        ("Янтарь", Color.FromRgb(0xFF, 0xB9, 0x00)),   // как в Wooting
    };

    public static Color Default => Presets[0].Color;
    public static Color Current { get; private set; } = Presets[0].Color;

    public static void Apply(Color c)
    {
        Current = c;
        var res = Application.Current.Resources;
        res["Accent"] = new SolidColorBrush(c);
        res["AccentFg"] = new SolidColorBrush(IsLight(c)
            ? Color.FromRgb(0x09, 0x09, 0x0B)    // тёмный текст на светлом акценте
            : Color.FromRgb(0xFA, 0xFA, 0xFA));  // светлый — на тёмном
        Settings.AccentHex = ToHex(c);
    }

    public static void Load()
    {
        var saved = Settings.AccentHex;
        Apply(TryParse(saved, out var c) ? c : Default);
    }

    /// <summary>Относительная яркость (WCAG) — по ней выбираем цвет текста поверх акцента.</summary>
    private static bool IsLight(Color c)
        => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0 > 0.55;

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static bool TryParse(string? hex, out Color c)
    {
        c = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try { c = (Color)ColorConverter.ConvertFromString(hex); return true; }
        catch { return false; }
    }
}
