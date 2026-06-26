using System.Windows.Media;
using AntiLagPro.Core;

namespace AntiLagPro.App;

/// <summary>Строка DNS-сервера для UI (имя, IP, отклик, цвет).</summary>
public sealed class DnsRow
{
    public DnsServer Server { get; }
    public long Ms { get; }

    public DnsRow(DnsServer server, long ms = -2)   // -2 = ещё не замеряли
    {
        Server = server;
        Ms = ms;
    }

    public string Name => Server.Name;
    public string Ips => $"{Server.Primary} / {Server.Secondary}";
    public string LatencyText => Ms == -2 ? "—" : Ms < 0 ? "нет ответа" : $"{Ms} мс";

    public Brush Accent =>
        Ms < 0 ? Gray :
        Ms < 40 ? Green :
        Ms < 80 ? Yellow : Orange;

    private static readonly Brush Green  = new SolidColorBrush(Color.FromRgb(0x1F, 0x9E, 0x58));
    private static readonly Brush Yellow = new SolidColorBrush(Color.FromRgb(0xD9, 0xA5, 0x3A));
    private static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xC9, 0x6A, 0x2E));
    private static readonly Brush Gray   = new SolidColorBrush(Color.FromRgb(0x85, 0x8E, 0x9E));
}
