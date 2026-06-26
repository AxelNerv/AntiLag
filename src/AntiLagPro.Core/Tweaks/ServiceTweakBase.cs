using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// База для твиков «отключить службу». Меняет тип запуска службы в реестре
/// (Start: 2=авто, 3=вручную, 4=отключено) и останавливает её сейчас.
/// Откат возвращает прежний тип запуска.
/// </summary>
public abstract class ServiceTweakBase : ITweak
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    protected abstract string ServiceName { get; }

    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => false;

    private string KeyPath => $@"SYSTEM\CurrentControlSet\Services\{ServiceName}";

    public bool IsApplied()
    {
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath);
        return k?.GetValue("Start") is int s && s == 4; // 4 = disabled
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true)
            ?? throw new InvalidOperationException($"Служба {ServiceName} не найдена.");

        int old = k.GetValue("Start") is int s ? s : 3;
        slot["start"] = old.ToString();

        k.SetValue("Start", 4, RegistryValueKind.DWord);     // отключить
        ProcessRunner.Run("sc.exe", $"stop {ServiceName}");  // остановить сейчас (ошибку игнорируем)
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
        if (k is not null && int.TryParse(slot.GetValueOrDefault("start"), out int old))
        {
            k.SetValue("Start", old, RegistryValueKind.DWord);
            ProcessRunner.Run("sc.exe", $"config {ServiceName} start= {StartFlag(old)}");
        }
        backup.Remove(Id);
    }

    private static string StartFlag(int s) => s switch
    {
        2 => "auto",
        3 => "demand",
        4 => "disabled",
        _ => "demand"
    };
}
