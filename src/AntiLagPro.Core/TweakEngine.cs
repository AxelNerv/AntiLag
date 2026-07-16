using System.Security.Principal;
using AntiLagPro.Core.Tweaks;

namespace AntiLagPro.Core;

/// <summary>Состояние одного твика для показа в UI/CLI.</summary>
public sealed record TweakState(ITweak Tweak, bool IsApplied);

/// <summary>
/// Центральный движок: список твиков + применение/откат с общим бэкапом.
/// UI (WPF) и CLI работают через него одинаково.
/// </summary>
public sealed class TweakEngine
{
    public IReadOnlyList<ITweak> Tweaks { get; }
    public TimerResolutionService Timer { get; } = new();

    public TweakEngine()
    {
        Tweaks = new ITweak[]
        {
            // Базовое (универсально и безопасно)
            new GlobalTimerResolutionTweak(),
            new PowerPlanTweak(),
            // Игровые оптимизации (опционально)
            new NetworkPowerTweak(),
            new GameBarTweak(),
            new SysMainTweak(),
            new SearchIndexTweak(),
            new KeyboardSpeedTweak(),
            new InputLagTweak(),
            new ForegroundPriorityTweak(),
            new CoreParkingTweak(),
            new SystemResponsivenessTweak(),
            new NagleTweak(),
            new PowerThrottlingTweak(),
            new MmcssGamesTweak(),
            new NicLatencyTweak(),
            new DynamicTickTweak(),
            new InputQueueTweak(),
        };
    }

    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public IEnumerable<TweakState> GetStatus()
        => Tweaks.Select(t => new TweakState(t, SafeIsApplied(t)));

    private static bool SafeIsApplied(ITweak t)
    {
        try { return t.IsApplied(); } catch { return false; }
    }

    /// Применить выбранные твики (по Id). Бэкап сохраняется на диск.
    public bool RequiresRebootAfter { get; private set; }

    public void Apply(IEnumerable<string> tweakIds)
    {
        RequireAdmin();
        var backup = BackupStore.Load();
        RequiresRebootAfter = false;

        foreach (var t in Tweaks.Where(t => tweakIds.Contains(t.Id)))
        {
            if (t.IsApplied()) continue;          // уже применён — пропускаем
            t.Apply(backup);
            if (t.RequiresReboot) RequiresRebootAfter = true;
        }

        BackupStore.Save(backup);
        // Удержание 0.5 ms (Timer) — отдельная история, управляется из UI/CLI явно.
    }

    /// Откатить выбранные твики (или все, если ids = null).
    public void Restore(IEnumerable<string>? tweakIds = null)
    {
        RequireAdmin();
        var backup = BackupStore.Load();

        foreach (var t in Tweaks)
        {
            if (tweakIds is not null && !tweakIds.Contains(t.Id)) continue;
            t.Restore(backup);
        }

        BackupStore.Save(backup);
    }

    private static void RequireAdmin()
    {
        if (!IsElevated())
            throw new UnauthorizedAccessException("Нужны права администратора.");
    }
}
