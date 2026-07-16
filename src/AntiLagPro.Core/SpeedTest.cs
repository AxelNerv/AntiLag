using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace AntiLagPro.Core;

public enum SpeedServer { SelectelRu, Cloudflare }

public sealed record SpeedResult(double DownMbps, double UpMbps, long PingMs, bool Ok, string? Error = null);

/// <summary>
/// Замер скорости интернета в НЕСКОЛЬКО потоков (как Яндекс/Speedtest) — одно
/// TCP-соединение не насыщает гигабит. По умолчанию RU-сервер Selectel (LibreSpeed):
/// он реально выдаёт ~900↓/850↑ на 1 Гбит/с. Cloudflare оставлен как глобальный
/// вариант для пользователей вне РФ (у части RU-провайдеров он режет скорость).
/// </summary>
public static class SpeedTest
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 128,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    })
    { Timeout = TimeSpan.FromSeconds(60) };

    static SpeedTest()
    {
        // Без браузерного UA Cloudflare отвечает 403.
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
    }

    private const int DownConns = 8;    // потоков на загрузку
    private const int UpConns = 16;     // потоков на отдачу (нужно больше, чтобы насытить upload)
    private const int Seconds = 7;       // длительность каждого замера

    public static async Task<SpeedResult> Run(SpeedServer server, IProgress<string> log)
    {
        try
        {
            log.Report("Пинг…");
            long ping = PingMs(server);

            log.Report("Замер загрузки…");
            double down = await Measure(true, server, DownConns);

            log.Report("Замер отдачи…");
            double up = await Measure(false, server, UpConns);

            log.Report("Готово.");
            return new SpeedResult(down, up, ping, true);
        }
        catch (Exception ex)
        {
            return new SpeedResult(0, 0, 0, false, ex.Message);
        }
    }

    private static async Task<double> Measure(bool download, SpeedServer server, int conns)
    {
        long total = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Seconds));
        var sw = Stopwatch.StartNew();

        async Task Worker()
        {
            var buf = new byte[131072];
            var upChunk = download ? Array.Empty<byte>() : new byte[5_000_000];
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    if (download)
                    {
                        using var resp = await Http.GetAsync(DownUrl(server), HttpCompletionOption.ResponseHeadersRead, cts.Token);
                        using var st = await resp.Content.ReadAsStreamAsync(cts.Token);
                        int n;
                        while ((n = await st.ReadAsync(buf, cts.Token)) > 0) Interlocked.Add(ref total, n);
                    }
                    else
                    {
                        using var c = new ByteArrayContent(upChunk);
                        using var resp = await Http.PostAsync(UpUrl(server), c, cts.Token);
                        if (resp.IsSuccessStatusCode) Interlocked.Add(ref total, upChunk.Length);
                    }
                }
                catch { /* по таймауту/отмене просто выходим */ }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, conns).Select(_ => Worker()));
        sw.Stop();
        double secs = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        return total * 8.0 / 1_000_000.0 / secs; // Мбит/с
    }

    private static string DownUrl(SpeedServer s) => s switch
    {
        SpeedServer.Cloudflare => $"https://speed.cloudflare.com/__down?bytes=200000000&r={Guid.NewGuid():N}",
        _                      => $"https://speedtest.selectel.ru/1GB?r={Guid.NewGuid():N}"
    };

    private static string UpUrl(SpeedServer s) => s switch
    {
        SpeedServer.Cloudflare => $"https://speed.cloudflare.com/__up?r={Guid.NewGuid():N}",
        _                      => $"https://speedtest.selectel.ru/empty.php?r={Guid.NewGuid():N}"
    };

    // TCP-connect пинг до сервера замера. Имя резолвим ЗАРАНЕЕ (обычный DNS,
    // мимо туннеля), а коннектимся уже по IP — иначе DNS через TUN отваливается,
    // а ICMP через gvisor даёт ложные 0. По IP проходит и в туннеле.
    private static long PingMs(SpeedServer s)
    {
        string host = s == SpeedServer.Cloudflare ? "speed.cloudflare.com" : "speedtest.selectel.ru";
        IPAddress? ip = null;
        try { ip = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork); }
        catch { }
        if (ip is null) return -1;

        long best = -1;
        for (int i = 0; i < 3; i++)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var tcp = new TcpClient();
                var t = tcp.ConnectAsync(ip, 443);
                if (t.Wait(3000) && tcp.Connected)
                {
                    sw.Stop();
                    long ms = sw.ElapsedMilliseconds;
                    if (best < 0 || ms < best) best = ms;
                }
            }
            catch { }
        }
        return best;
    }
}
