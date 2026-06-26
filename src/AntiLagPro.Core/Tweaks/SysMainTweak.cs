namespace AntiLagPro.Core.Tweaks;

/// <summary>Отключить SuperFetch / SysMain (служба упреждающего кэширования).</summary>
public sealed class SysMainTweak : ServiceTweakBase
{
    public override string Id => "svc-sysmain";
    public override string Name => "Отключить SuperFetch (SysMain)";
    public override string Description =>
        "Освобождает RAM и нагрузку на диск. Не рекомендуется, если у тебя HDD " +
        "и программы стали запускаться медленнее.";
    protected override string ServiceName => "SysMain";
}
