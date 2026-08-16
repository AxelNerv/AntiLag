namespace AntiLagPro.Core;

/// <summary>
/// Держит разрешение таймера на 0.5 ms, пока процесс жив — поэтому программа и
/// сидит в трее. Чтобы эффект был системным, нужен ещё флаг
/// GlobalTimerResolutionRequests (см. GlobalTimerResolutionTweak) и перезагрузка.
/// </summary>
public sealed class TimerResolutionService : IDisposable
{
    private const uint Target = 5000; // 0.5 ms

    // Проверяем часто: отрисовка окна периодически сбивает удержание, а повторный
    // запрос стоит копейки.
    private static readonly TimeSpan WatchInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ComplainEvery = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private System.Threading.Timer? _watchdog;
    private bool _active;
    private bool _prepared;
    private DateTime? _failingSince;
    private DateTime _lastComplaint;

    public double CurrentMs => Native.QueryResolutionMs().current;
    public double MinMs => Native.QueryResolutionMs().min;
    public bool IsActive => _active;

    /// <summary>Держится ли 0.5 ms на самом деле, а не только по нашему флагу.</summary>
    public bool IsHeld => CurrentMs <= 0.6;

    /// <summary>Запросить 0.5 ms и держать. Повторный вызов безвреден.</summary>
    public void Start()
    {
        lock (_gate)
        {
            Prepare();
            if (!Request()) return;

            _active = true;
            _watchdog ??= new System.Threading.Timer(_ => Guard(), null, WatchInterval, WatchInterval);
        }
    }

    /// <summary>Вернуть системе обычный таймер.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _watchdog?.Dispose();
            _watchdog = null;
            if (!_active) return;

            int status = Native.NtSetTimerResolution(Target, false, out _);
            if (status != 0) Log.Warn($"Не удалось вернуть таймер системе (код {status:X8})");
            _active = false;
        }
    }

    /// <summary>Проверить удержание прямо сейчас и вернуть 0.5 ms, если слетело.</summary>
    public void EnsureHeld() => Guard();

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void Prepare()
    {
        if (_prepared) return;

        _prepared = Native.RelaxThrottling();
        if (!_prepared)
            Log.Warn("Система продолжает игнорировать запрос точного таймера — удержать 0.5 ms не выйдет");
    }

    /// <summary>
    /// Сбрасывать запрос перед повторной установкой НЕЛЬЗЯ: при включённом флаге
    /// GlobalTimerResolutionRequests сброс роняет разрешение всей системы.
    /// </summary>
    private bool Request()
    {
        int status = Native.NtSetTimerResolution(Target, true, out _);
        if (status != 0)
        {
            Log.Error($"Не удалось запросить таймер 0.5 ms (код {status:X8})");
            _active = false;
            return false;
        }
        return true;
    }

    /// <summary>Если удержание слетело — вернуть молча.</summary>
    private void Guard()
    {
        lock (_gate)
        {
            if (!_active) return;
            if (IsHeld) { _failingSince = null; return; }

            Request();
            if (IsHeld) { _failingSince = null; return; }

            // Жалуемся, только если не удаётся уже долго подряд.
            _failingSince ??= DateTime.UtcNow;
            if (DateTime.UtcNow - _failingSince >= Grace) Complain();
        }
    }

    private void Complain()
    {
        if (DateTime.UtcNow - _lastComplaint < ComplainEvery) return;
        _lastComplaint = DateTime.UtcNow;

        var (cur, min, max) = Native.QueryResolutionMs();
        Log.Warn($"Система не отдаёт 0.5 ms: сейчас {cur:N4} ms " +
                 $"(возможный минимум {min:N4}, максимум {max:N4}); троттлинг снят: {_prepared}");
    }
}
