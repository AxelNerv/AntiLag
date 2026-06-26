using System.Net.NetworkInformation;

namespace AntiLagPro.Core;

/// <summary>
/// Живой мониторинг соединения: каждую секунду пингует хост (по умолч. 8.8.8.8),
/// держит скользящее окно последних замеров и считает задержку, потери, джиттер,
/// пики. Для карточки «Состояние соединения» (как у ExitLag).
/// </summary>
public sealed class ConnectionMonitor
{
    private const int Window = 60;       // сколько последних замеров держим
    private readonly object _lock = new();
    private readonly Queue<long> _samples = new();   // мс задержки, -1 = потеря
    private Thread? _thread;
    private volatile bool _run;
    private string _host = "8.8.8.8";

    public bool IsRunning => _run;
    public double AvgLatency { get; private set; }
    public double LossPercent { get; private set; }
    public double Jitter { get; private set; }
    public int Peaks { get; private set; }
    public string Status { get; private set; } = "—";
    public int StatusLevel { get; private set; }     // 0 = ок, 1 = средне, 2 = плохо

    public void Start(string host = "8.8.8.8")
    {
        if (_run) return;
        _host = host;
        lock (_lock) _samples.Clear();
        _run = true;
        _thread = new Thread(Loop) { IsBackground = true };
        _thread.Start();
    }

    public void Stop()
    {
        _run = false;
        _thread?.Join(1500);
        _thread = null;
    }

    private void Loop()
    {
        using var ping = new Ping();
        while (_run)
        {
            long ms;
            try
            {
                var r = ping.Send(_host, 1500);
                ms = r.Status == IPStatus.Success ? r.RoundtripTime : -1;
            }
            catch { ms = -1; }

            lock (_lock)
            {
                _samples.Enqueue(ms);
                while (_samples.Count > Window) _samples.Dequeue();
                Compute();
            }
            Thread.Sleep(1000);
        }
    }

    private void Compute()
    {
        var arr = _samples.ToArray();
        if (arr.Length == 0) return;

        var ok = arr.Where(x => x >= 0).ToArray();
        int loss = arr.Count(x => x < 0);

        LossPercent = loss * 100.0 / arr.Length;
        AvgLatency = ok.Length > 0 ? ok.Average() : 0;

        // Джиттер = средняя |разница| между соседними удачными замерами.
        double jit = 0; int n = 0;
        for (int i = 1; i < arr.Length; i++)
            if (arr[i] >= 0 && arr[i - 1] >= 0) { jit += Math.Abs(arr[i] - arr[i - 1]); n++; }
        Jitter = n > 0 ? jit / n : 0;

        // Пики = замеры заметно выше среднего.
        Peaks = ok.Count(x => x > AvgLatency * 2 && x > 50);

        if (LossPercent < 2 && Jitter < 15 && AvgLatency < 80) { Status = "Стабильный"; StatusLevel = 0; }
        else if (LossPercent > 10 || Jitter > 50)              { Status = "Нестабильный"; StatusLevel = 2; }
        else                                                    { Status = "Средний"; StatusLevel = 1; }
    }
}
