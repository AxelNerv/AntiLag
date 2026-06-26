using System.Windows;
using System.Windows.Media;
using AntiLagPro.Core;

namespace AntiLagPro.App;

/// <summary>Обёртка над Finding для UI: добавляет цвет и видимость строки «фикс».</summary>
public sealed class FindingRow
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Fix { get; init; } = "";
    public Brush Accent { get; init; } = Brushes.Gray;
    public string GoTo { get; init; } = "";

    public Visibility FixVisibility => string.IsNullOrEmpty(Fix) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GoToVisibility => string.IsNullOrEmpty(GoTo) ? Visibility.Collapsed : Visibility.Visible;

    public static FindingRow From(Finding f)
    {
        Brush accent = f.Severity switch
        {
            Severity.Ok   => new SolidColorBrush(Color.FromRgb(0x1F, 0x9E, 0x58)),
            Severity.Warn => new SolidColorBrush(Color.FromRgb(0xD9, 0xA5, 0x3A)),
            Severity.Bad  => new SolidColorBrush(Color.FromRgb(0xD6, 0x45, 0x45)),
            _             => new SolidColorBrush(Color.FromRgb(0x5A, 0x8E, 0xD6)),
        };
        return new FindingRow
        {
            Title = f.Title,
            Detail = f.Detail,
            Fix = string.IsNullOrEmpty(f.Fix) ? "" : "→ " + f.Fix,
            Accent = accent,
            GoTo = f.GoTo ?? ""
        };
    }
}
