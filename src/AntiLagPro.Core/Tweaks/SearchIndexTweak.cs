namespace AntiLagPro.Core.Tweaks;

/// <summary>Отключить индексацию файлов Windows (служба Windows Search).</summary>
public sealed class SearchIndexTweak : ServiceTweakBase
{
    public override string Id => "svc-wsearch";
    public override string Name => "Отключить индексацию Windows (поиск)";
    public override string Description =>
        "Освобождает диск, убирает фоновую индексацию. Не рекомендуется, если ты " +
        "часто пользуешься поиском по файлам в проводнике.";
    protected override string ServiceName => "WSearch";
}
