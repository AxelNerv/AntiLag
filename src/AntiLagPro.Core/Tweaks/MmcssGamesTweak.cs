using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Повышенный приоритет игровых задач в планировщике мультимедиа (MMCSS):
/// GPU Priority=8, Priority=6, категория High. Игровые потоки получают CPU/GPU
/// раньше фоновых — стабильнее фреймтайм. Полностью обратимо.
/// </summary>
public sealed class MmcssGamesTweak : ITweak
{
    public string Id => "mmcss-games";
    public string Name => "Приоритет игр (MMCSS)";
    public string Description =>
        "Поднимает приоритет игровых задач в системном планировщике мультимедиа " +
        "(GPU Priority 8, Priority 6, категория High) — игра получает ресурсы раньше " +
        "фоновых процессов, стабильнее фреймтайм.";
    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => false;

    private const string KeyPath =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";

    public bool IsApplied()
    {
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath);
        return k?.GetValue("GPU Priority") is int g && g == 8
            && k.GetValue("Priority") is int p && p == 6
            && k.GetValue("Scheduling Category") as string == "High";
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true)
            ?? Registry.LocalMachine.CreateSubKey(KeyPath);

        slot["gpu"]  = (k.GetValue("GPU Priority") as int?)?.ToString();
        slot["prio"] = (k.GetValue("Priority") as int?)?.ToString();
        slot["cat"]  = k.GetValue("Scheduling Category") as string;
        slot["sfio"] = k.GetValue("SFIO Priority") as string;

        k.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
        k.SetValue("Priority", 6, RegistryValueKind.DWord);
        k.SetValue("Scheduling Category", "High", RegistryValueKind.String);
        k.SetValue("SFIO Priority", "High", RegistryValueKind.String);
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
        if (k is not null)
        {
            k.SetValue("GPU Priority", int.TryParse(slot.GetValueOrDefault("gpu"), out int g) ? g : 2, RegistryValueKind.DWord);
            k.SetValue("Priority", int.TryParse(slot.GetValueOrDefault("prio"), out int p) ? p : 2, RegistryValueKind.DWord);
            k.SetValue("Scheduling Category", slot.GetValueOrDefault("cat") ?? "Medium", RegistryValueKind.String);
            k.SetValue("SFIO Priority", slot.GetValueOrDefault("sfio") ?? "Normal", RegistryValueKind.String);
        }
        backup.Remove(Id);
    }
}
