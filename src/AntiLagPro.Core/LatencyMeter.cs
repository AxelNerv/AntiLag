using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AntiLagPro.Core;

/// <summary>
/// Приблизительный измеритель задержки (user-mode, без драйвера).
/// Поток с высоким приоритетом ждёт ~1 мс и смотрит реальное время; превышение
/// над 1 мс = система нас "тормознула". Максимум считается за СКОЛЬЗЯЩЕЕ окно
/// (5 сек), чтобы один старый спайк не висел вечно. На простое CPU спайки —
/// это норма (ядро уходит в сон), показателен замер под нагрузкой.
/// </summary>
public sealed class LatencyMeter
{
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] private static extern bool SetThreadPriority(IntPtr handle, int priority);
    private const int THREAD_PRIORITY_TIME_CRITICAL = 15;

    private const int WindowSamples = 5000;   // ~5 секунд при шаге 1 мс
    private readonly Queue<int> _window = new();
    private Thread? _thread;
    private volatile bool _run;
    private volatile bool _reset;
    private volatile int _currentUs;
    private volatile int _maxUs;

    public bool IsRunning => _run;
    public double CurrentUs => _currentUs;
    public double MaxUs => _maxUs;

    public void Start()
    {
        if (_run) return;
        _run = true;
        _maxUs = 0;
        _currentUs = 0;
        _reset = true;
        _thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.Highest };
        _thread.Start();
    }

    public void Stop()
    {
        _run = false;
        _thread?.Join(1000);
        _thread = null;
    }

    public void ResetMax()
    {
        _maxUs = 0;       // сразу обнуляем (чтобы работало и когда замер остановлен)
        _currentUs = 0;
        _reset = true;    // окно очистит фоновый поток (если запущен)
    }

    private void Loop()
    {
        SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);
        double freq = Stopwatch.Frequency;
        var sw = Stopwatch.StartNew();

        while (_run)
        {
            if (_reset) { _window.Clear(); _maxUs = 0; _reset = false; }

            long start = sw.ElapsedTicks;
            Thread.Sleep(1);
            long end = sw.ElapsedTicks;

            double ms = (end - start) * 1000.0 / freq;
            int us = (int)Math.Max(0, (ms - 1.0) * 1000.0);

            _currentUs = us;
            _window.Enqueue(us);
            while (_window.Count > WindowSamples) _window.Dequeue();

            int m = 0;
            foreach (var v in _window) if (v > m) m = v;
            _maxUs = m;
        }
    }
}
