namespace AntiLagPro.Core;

/// <summary>
/// Держит Timer Resolution на 0.5 ms, пока процесс жив.
/// ВАЖНО: разрешение таймера действует только пока процесс, который его
/// запросил, работает. Поэтому в готовой тулзе этот сервис живёт в фоне (трей).
/// Чтобы эффект был системным (а не только для процесса), нужен ещё флаг
/// GlobalTimerResolutionRequests (см. GlobalTimerResolutionTweak) + перезагрузка.
/// </summary>
public sealed class TimerResolutionService
{
    private const uint Target = 5000; // 0.5 ms в тиках по 100 нс
    private bool _active;

    /// Текущее разрешение таймера в миллисекундах (то, что реально стоит в системе).
    public double CurrentMs => Native.QueryResolutionMs().current;
    public double MinMs => Native.QueryResolutionMs().min;
    public bool IsActive => _active;

    /// Запросить 0.5 ms. Держится, пока процесс жив (или пока не вызван Stop()).
    public void Start()
    {
        Native.NtSetTimerResolution(Target, true, out _);
        _active = true;
    }

    public void Stop()
    {
        Native.NtSetTimerResolution(Target, false, out _);
        _active = false;
    }
}
