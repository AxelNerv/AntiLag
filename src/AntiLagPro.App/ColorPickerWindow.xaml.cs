using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AntiLagPro.App;

/// <summary>
/// Свой выбор цвета в стиле приложения (системный диалог Windows выглядит чужеродно).
/// Модель HSV: поле — насыщенность (X) и яркость (Y), полоса снизу — оттенок.
/// </summary>
public partial class ColorPickerWindow : Window
{
    private double _h, _s = 1, _v = 1;   // 0..360, 0..1, 0..1
    private bool _updating;

    public Color SelectedColor { get; private set; }

    public ColorPickerWindow(Color initial)
    {
        InitializeComponent();
        Header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        SizeChanged += (_, _) => Redraw();
        (_h, _s, _v) = ToHsv(initial);
        SelectedColor = initial;
        Loaded += (_, _) => Redraw();
    }

    // ---------- Ввод ----------
    private void Sv_Down(object s, MouseButtonEventArgs e) { SvArea.CaptureMouse(); SetSv(e.GetPosition(SvArea)); }
    private void Sv_Move(object s, MouseEventArgs e) { if (SvArea.IsMouseCaptured) SetSv(e.GetPosition(SvArea)); }
    private void Sv_Up(object s, MouseButtonEventArgs e) => SvArea.ReleaseMouseCapture();

    private void Hue_Down(object s, MouseButtonEventArgs e) { HueArea.CaptureMouse(); SetHue(e.GetPosition(HueArea)); }
    private void Hue_Move(object s, MouseEventArgs e) { if (HueArea.IsMouseCaptured) SetHue(e.GetPosition(HueArea)); }
    private void Hue_Up(object s, MouseButtonEventArgs e) => HueArea.ReleaseMouseCapture();

    private void SetSv(Point p)
    {
        _s = Math.Clamp(p.X / Math.Max(SvArea.ActualWidth, 1), 0, 1);
        _v = 1 - Math.Clamp(p.Y / Math.Max(SvArea.ActualHeight, 1), 0, 1);
        Redraw();
    }

    private void SetHue(Point p)
    {
        _h = Math.Clamp(p.X / Math.Max(HueArea.ActualWidth, 1), 0, 1) * 360;
        Redraw();
    }

    private void Hex_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Hex_Commit(s, e);
        e.Handled = true;
    }

    private void Hex_Commit(object s, RoutedEventArgs e)
    {
        if (_updating) return;
        if (Theme.TryParse(HexBox.Text.Trim(), out var c)) { (_h, _s, _v) = ToHsv(c); Redraw(); }
        else Redraw();   // вернуть корректное значение обратно в поле
    }

    // ---------- Отрисовка ----------
    private void Redraw()
    {
        if (!IsLoaded) return;
        var c = FromHsv(_h, _s, _v);
        SelectedColor = c;

        HueFill.Fill = new SolidColorBrush(FromHsv(_h, 1, 1));
        Preview.Background = new SolidColorBrush(c);

        Canvas.SetLeft(SvCursor, _s * SvArea.ActualWidth - SvCursor.Width / 2);
        Canvas.SetTop(SvCursor, (1 - _v) * SvArea.ActualHeight - SvCursor.Height / 2);
        Canvas.SetLeft(HueCursor, _h / 360 * HueArea.ActualWidth - HueCursor.Width / 2);

        _updating = true;
        HexBox.Text = Theme.ToHex(c);
        _updating = false;
    }

    private void Ok_Click(object s, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Cancel_Click(object s, RoutedEventArgs e) { DialogResult = false; Close(); }

    // ---------- HSV <-> RGB ----------
    private static (double h, double s, double v) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min, h = 0;

        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * (((b - r) / d) + 2);
            else h = 60 * (((r - g) / d) + 4);
        }
        if (h < 0) h += 360;
        return (h, max <= 0 ? 0 : d / max, max);
    }

    private static Color FromHsv(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60 % 2) - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;

        switch ((int)(h / 60) % 6)
        {
            case 0: r = c; g = x; break;
            case 1: r = x; g = c; break;
            case 2: g = c; b = x; break;
            case 3: g = x; b = c; break;
            case 4: r = x; b = c; break;
            default: r = c; b = x; break;
        }
        return Color.FromRgb((byte)Math.Round((r + m) * 255),
                             (byte)Math.Round((g + m) * 255),
                             (byte)Math.Round((b + m) * 255));
    }
}
