using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AntiLagPro.Core;

/// <summary>
/// Сетевые инструменты (user-mode, без VPN): Ping, TCPing, Трассировка
/// и быстрый скан сети на ошибки. Длинные операции пишут результат построчно
/// через IProgress, поэтому вызывать их нужно в фоне (Task.Run).
/// </summary>
public static class NetworkTools
{
    public static void Ping(string host, IProgress<string> log, int count = 4)
    {
        log.Report($"Ping {host}");
        long sum = 0; int ok = 0;
        using var ping = new Ping();
        for (int i = 0; i < count; i++)
        {
            try
            {
                var r = ping.Send(host, 2000);
                if (r.Status == IPStatus.Success)
                {
                    log.Report($"  ответ от {r.Address}: {r.RoundtripTime} ms");
                    sum += r.RoundtripTime; ok++;
                }
                else log.Report($"  {r.Status}");
            }
            catch (Exception ex) { log.Report($"  ошибка: {ex.Message}"); }
            Thread.Sleep(300);
        }
        log.Report(ok > 0 ? $"Среднее: {sum / ok} ms, потеряно {count - ok}/{count}" : "Нет ответа.");
    }

    public static void TcpPing(string host, int port, IProgress<string> log, int count = 4)
    {
        log.Report($"TCPing {host}:{port}");
        long sum = 0; int ok = 0;
        for (int i = 0; i < count; i++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var c = new TcpClient();
                if (c.ConnectAsync(host, port).Wait(2000) && c.Connected)
                {
                    sw.Stop();
                    log.Report($"  подключение: {sw.ElapsedMilliseconds} ms");
                    sum += sw.ElapsedMilliseconds; ok++;
                }
                else log.Report("  таймаут");
            }
            catch (Exception ex) { log.Report($"  ошибка: {ex.Message}"); }
            Thread.Sleep(300);
        }
        log.Report(ok > 0 ? $"Среднее: {sum / ok} ms, неудач {count - ok}/{count}" : "Не удалось подключиться.");
    }

    public static void TraceRoute(string host, IProgress<string> log, int maxHops = 30)
    {
        log.Report($"Трассировка до {host}");
        IPAddress? target;
        try { target = Dns.GetHostAddresses(host).FirstOrDefault(); }
        catch { target = null; }
        if (target is null) { log.Report("  не удалось разрешить имя."); return; }

        using var ping = new Ping();
        var buffer = new byte[32];
        for (int ttl = 1; ttl <= maxHops; ttl++)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var r = ping.Send(target, 3000, buffer, new PingOptions(ttl, true));
                sw.Stop();
                string addr = r.Address?.ToString() ?? "*";
                log.Report($"  {ttl,2}. {addr}  {sw.ElapsedMilliseconds} ms  [{r.Status}]");
                if (r.Status == IPStatus.Success) break;
            }
            catch (Exception ex) { log.Report($"  {ttl,2}. ошибка: {ex.Message}"); }
        }
        log.Report("Готово.");
    }

    /// <summary>Быстрая проверка сети: шлюз, интернет, DNS, ошибки адаптера.</summary>
    public static List<Finding> ScanNetwork()
    {
        var list = new List<Finding>();

        var nic = GetActiveInterface();
        if (nic is null)
        {
            list.Add(new Finding(Severity.Bad, "Сеть", "Активное подключение не найдено."));
            return list;
        }

        var gw = nic.GetIPProperties().GatewayAddresses
            .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;

        if (gw is not null)
        {
            var (gwOk, gwMs) = PingOnce(gw.ToString());
            list.Add(gwOk
                ? new Finding(Severity.Ok, $"Шлюз {gw}", $"отклик {gwMs} ms")
                : new Finding(Severity.Bad, $"Шлюз {gw}", "нет отклика — проблема с роутером/кабелем."));
        }

        var (netOk, netMs) = PingOnce("8.8.8.8");
        if (netOk)
            list.Add(new Finding(netMs > 60 ? Severity.Warn : Severity.Ok, "Интернет (8.8.8.8)",
                $"пинг {netMs} ms" + (netMs > 60 ? " — высоковато" : "")));
        else
            list.Add(new Finding(Severity.Bad, "Интернет (8.8.8.8)", "нет ответа."));

        try { Dns.GetHostEntry("www.google.com"); list.Add(new Finding(Severity.Ok, "DNS", "разрешение имён работает.")); }
        catch { list.Add(new Finding(Severity.Bad, "DNS", "не удалось разрешить имя — проблема с DNS.")); }

        var st = nic.GetIPStatistics();
        long errs = st.IncomingPacketsWithErrors + st.OutgoingPacketsWithErrors;
        list.Add(errs > 0
            ? new Finding(Severity.Warn, $"Адаптер {nic.Name}", $"ошибок пакетов: {errs} (возможны помехи/плохой кабель).")
            : new Finding(Severity.Ok, $"Адаптер {nic.Name}", "ошибок пакетов нет."));

        return list;
    }

    /// <summary>GUID активного интернет-адаптера (для твиков вроде Nagle).</summary>
    public static string? GetActiveInterfaceId() => GetActiveInterface()?.Id;

    /// <summary>Дружественное имя активного адаптера (для netsh).</summary>
    public static string? GetActiveInterfaceName() => GetActiveInterface()?.Name;

    /// <summary>Текущие IPv4 DNS активного адаптера.</summary>
    public static string[] GetActiveDns()
    {
        var nic = GetActiveInterface();
        if (nic is null) return Array.Empty<string>();
        return nic.GetIPProperties().DnsAddresses
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.ToString()).ToArray();
    }

    private static (bool ok, long ms) PingOnce(string host)
    {
        try { using var p = new Ping(); var r = p.Send(host, 2000); return (r.Status == IPStatus.Success, r.RoundtripTime); }
        catch { return (false, 0); }
    }

    /// <summary>
    /// Реальный интернет-адаптер: открываем UDP-сокет на 8.8.8.8 и смотрим, с какого
    /// локального IP он пошёл — это и есть активный адаптер (не Radmin/VPN/виртуальный).
    /// </summary>
    private static NetworkInterface? GetActiveInterface()
    {
        IPAddress? local = null;
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 65530);
            local = (s.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch { /* нет сети */ }

        var all = NetworkInterface.GetAllNetworkInterfaces();
        if (local is not null)
        {
            var match = all.FirstOrDefault(n =>
                n.GetIPProperties().UnicastAddresses.Any(u => u.Address.Equals(local)));
            if (match is not null) return match;
        }

        // запасной вариант: первый рабочий не-loopback с IPv4-шлюзом
        return all.FirstOrDefault(n =>
            n.OperationalStatus == OperationalStatus.Up &&
            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
            n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork));
    }
}
