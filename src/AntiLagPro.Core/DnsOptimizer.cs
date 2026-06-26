using System.Net.NetworkInformation;

namespace AntiLagPro.Core;

public sealed record DnsServer(string Name, string Primary, string Secondary, bool IsSystem = false);

/// <summary>Результат замера одного DNS (AvgMs = -1 если недоступен).</summary>
public sealed class DnsMeasure
{
    public DnsServer Server { get; init; } = default!;
    public long AvgMs { get; set; } = -1;
    public bool Reachable => AvgMs >= 0;
}

/// <summary>
/// DNS Optimizer: пингует список публичных DNS, ранжирует по отклику и умеет
/// применить выбранный на активном адаптере (netsh) или сбросить на DHCP.
/// </summary>
public static class DnsOptimizer
{
    public static readonly DnsServer[] Servers =
    {
        new("Cloudflare", "1.1.1.1", "1.0.0.1"),
        new("Google", "8.8.8.8", "8.8.4.4"),
        new("Quad9", "9.9.9.9", "149.112.112.112"),
        new("OpenDNS", "208.67.222.222", "208.67.220.220"),
        new("AdGuard", "94.140.14.14", "94.140.15.15"),
        new("Яндекс", "77.88.8.8", "77.88.8.1"),
        new("Verisign", "64.6.64.6", "64.6.65.6"),
        new("Comodo Secure", "8.26.56.26", "8.20.247.20"),
        new("CleanBrowsing", "185.228.168.9", "185.228.169.9"),
        new("DNS.Watch", "84.200.69.80", "84.200.70.40"),
        new("Level3", "4.2.2.1", "4.2.2.2"),
        new("Cloudflare Security", "1.1.1.2", "1.0.0.2"),
    };

    /// <summary>Текущий системный DNS (роутер) — отдельной строкой, как в ExitLag.</summary>
    public static DnsServer? GetSystemServer()
    {
        var dns = NetworkTools.GetActiveDns();
        if (dns.Length == 0) return null;
        string secondary = dns.Length > 1 ? dns[1] : dns[0];
        return new DnsServer("Системный (роутер)", dns[0], secondary, IsSystem: true);
    }

    /// <summary>Системный DNS (если есть) + все публичные.</summary>
    public static List<DnsServer> GetAllServers()
    {
        var list = new List<DnsServer>();
        var sys = GetSystemServer();
        if (sys is not null) list.Add(sys);
        list.AddRange(Servers);
        return list;
    }

    public static List<DnsMeasure> MeasureAll(int pings = 3)
    {
        var result = new List<DnsMeasure>();
        using var ping = new Ping();
        foreach (var s in GetAllServers())
        {
            long sum = 0; int ok = 0;
            for (int i = 0; i < pings; i++)
            {
                try
                {
                    var r = ping.Send(s.Primary, 1500);
                    if (r.Status == IPStatus.Success) { sum += r.RoundtripTime; ok++; }
                }
                catch { }
            }
            result.Add(new DnsMeasure { Server = s, AvgMs = ok > 0 ? sum / ok : -1 });
        }
        // Сначала самые быстрые, недоступные — в конец.
        return result.OrderBy(m => m.Reachable ? m.AvgMs : long.MaxValue).ToList();
    }

    public static void Apply(DnsServer s)
    {
        string name = NetworkTools.GetActiveInterfaceName()
            ?? throw new InvalidOperationException("Активный сетевой адаптер не найден.");
        ProcessRunner.Run("netsh", $"interface ipv4 set dnsservers name=\"{name}\" static {s.Primary} primary");
        ProcessRunner.Run("netsh", $"interface ipv4 add dnsservers name=\"{name}\" {s.Secondary} index=2");
        ProcessRunner.Run("ipconfig", "/flushdns");
    }

    public static void Reset()
    {
        string name = NetworkTools.GetActiveInterfaceName()
            ?? throw new InvalidOperationException("Активный сетевой адаптер не найден.");
        ProcessRunner.Run("netsh", $"interface ipv4 set dnsservers name=\"{name}\" dhcp");
        ProcessRunner.Run("ipconfig", "/flushdns");
    }
}
