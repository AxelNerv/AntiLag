using System.Windows;
using System.Windows.Media;
using AntiLagPro.Core;

namespace AntiLagPro.App;

/// <summary>Строка драйвера для UI (имя, версия/дата, статус-цвет, ссылка вендора).</summary>
public sealed class DriverRow
{
    public DriverInfo Info { get; }
    public DriverRow(DriverInfo info) => Info = info;

    public string Name => Info.Name;

    public string Details =>
        $"v{(string.IsNullOrEmpty(Info.Version) ? "?" : Info.Version)}   ·   " +
        $"{(string.IsNullOrEmpty(Info.DateStr) ? "дата неизв." : Info.DateStr)}";

    public string StatusText => Info.AgeLevel switch
    {
        2 => "сильно устарел",
        1 => "устарел",
        3 => "дата неизв.",
        4 => "системный (Windows)",
        _ => "актуален"
    };

    public Brush Accent => Info.AgeLevel switch
    {
        2 => Red,
        1 => Yellow,
        3 => Gray,
        4 => Gray,
        _ => Green
    };

    // Есть офиц. ссылка вендора → «Сайт»; иначе → «Найти» (поиск в Google по названию).
    public bool HasVendor => !string.IsNullOrEmpty(Info.VendorUrl);
    public string ActionText => HasVendor ? "Сайт" : "Найти";
    public string ActionUrl => HasVendor
        ? Info.VendorUrl!
        : "https://www.google.com/search?q=" + System.Uri.EscapeDataString(Info.Name + " driver download");

    private static readonly Brush Green  = new SolidColorBrush(Color.FromRgb(0x1F, 0x9E, 0x58));
    private static readonly Brush Yellow = new SolidColorBrush(Color.FromRgb(0xD9, 0xA5, 0x3A));
    private static readonly Brush Red    = new SolidColorBrush(Color.FromRgb(0xD6, 0x45, 0x45));
    private static readonly Brush Gray   = new SolidColorBrush(Color.FromRgb(0x85, 0x8E, 0x9E));
}
