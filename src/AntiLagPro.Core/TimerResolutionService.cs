namespace AntiLagPro.Core;

/// <summary>
/// Держит Timer Resolution на 0.5 ms, пока процесс жив.
/// ВАЖНО: разрешение таймера действует только пока процесс, который его
/// запросил, работает. Поэтому в готовой тулзе этот сервис живёт в фоне (трей).
/// Чтобы эффект был системным (а не только для процесса), нужен ещё флаг
/// GlobalTimerResolutionRequests (см. GlobalTimerResolutionTweak) + перезагрузка.
/// </summary>
public sealed class TimerResolutionService : IDisposable
{
    private const uint Target = 5000; // 0.5 ms в тиках по 100 нс

    /// Как часто сторож проверяет, что 0.5 ms всё ещё держится. Часто — потому что
    /// отрисовка окна (анимации) периодически сбивает удержание, а повторный запрос
    /// без сброса стоит копейки и системе не мешает.
    private static readonly TimeSpan WatchInterval = TimeSpan.FromMilliseconds(250);
    /// Сколько терпеть неудачи подряд, прежде чем писать в журнал.
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);
    /// Как часто разрешено жаловаться, если система упорно не отдаёт 0.5 ms.
    private static readonly TimeSpan ComplainEvery = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private System.Threading.Timer? _watchdog;
    private bool _active;
    private bool _prepared;          // процесс уже выведен из-под троттлинга
    private DateTime? _failingSince; // с какого момента подряд не удаётся удержать
    private DateTime _lastComplaint;

    /// Текущее разрешение таймера в миллисекундах (то, что реально стоит в системе).
    public double CurrentMs => Native.QueryResolutionMs().current;
    public double MinMs => Native.QueryResolutionMs().min;
    public bool IsActive => _active;

    /// <summary>Держится ли сейчас 0.5 ms на самом деле (а не только по нашему флагу).</summary>
    public bool IsHeld => CurrentMs <= 0.6;

    /// <summary>
    /// Запросить 0.5 ms и держать, пока процесс жив (или пока не вызван Stop()).
    /// Повторный вызов безвреден.
    /// </summary>
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

    /// <summary>Вернуть системе обычный таймер и распустить сторожа.</summary>
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

    /// <summary>
    /// Снять с процесса два ограничения Windows 11. Без этого запрос таймера
    /// принимается «для вида»: система отвечает успехом и остаётся на 1.0 ms.
    /// </summary>
    private void Prepare()
    {
        if (_prepared) return;

        _prepared = Native.RelaxThrottling();
        if (!_prepared)
            Log.Warn("Система продолжает игнорировать запрос точного таймера — удержать 0.5 ms не выйдет");
    }

    /// <summary>
    /// Запросить 0.5 ms. Сбрасывать запрос перед повторной установкой НЕЛЬЗЯ:
    /// при включённом флаге GlobalTimerResolutionRequests сброс роняет разрешение
    /// всей системы, и программа начинает мешать сама себе.
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

    /// <summary>Работа сторожа: если удержание слетело — вернуть молча.</summary>
    private void Guard()
    {
        lock (_gate)
        {
            if (!_active) return;
            if (IsHeld) { _failingSince = null; return; }

            Request();
            if (IsHeld) { _failingSince = null; return; }

            // Не удалось. Жалуемся, только если не удаётся уже долго подряд.
            _failingSince ??= DateTime.UtcNow;
            if (DateTime.UtcNow - _failingSince >= Grace) Complain();
        }
    }

    /// <summary>Пожаловаться в журнал, но не чаще раза в ComplainEvery.</summary>
    private void Complain()
    {
        if (DateTime.UtcNow - _lastComplaint < ComplainEvery) return;
        _lastComplaint = DateTime.UtcNow;

        var (cur, min, max) = Native.QueryResolutionMs();
        Log.Warn($"Система не отдаёт 0.5 ms: сейчас {cur:N4} ms " +
                 $"(возможный минимум {min:N4}, максимум {max:N4}); троттлинг снят: {_prepared}");
    }
}
