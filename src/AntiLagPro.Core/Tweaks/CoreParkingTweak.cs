using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Держать все ядра активными — отключает парковку ядер (CPMINCORES=100) на
/// активной схеме питания через реестр. Убирает лаг от "пробуждения" ядер.
/// (В нашей схеме «AntiLag» это уже включено — здесь отдельный тумблер.)
/// </summary>
public sealed class CoreParkingTweak : ITweak
{
    public string Id => "core-parking";
    public string Name => "Держать все ядра активными";
    public string Description =>
        "Отключает парковку ядер на активной схеме питания. Если уже включена схема «AntiLag» — этот твик дублирует её эффект.";
    public TweakTier Tier => TweakTier.Universal;
    public bool RequiresReboot => false;

    private const string SUB_PROCESSOR = "54533251-82be-4824-96c1-47b60b740d00";
    private const string CPMINCORES    = "0cc5b647-c1df-4637-891a-dec35c318583";

    private static readonly Regex GuidRx = new(
        @"[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}", RegexOptions.Compiled);

    private static string? ActiveScheme()
    {
        var m = GuidRx.Match(ProcessRunner.Powercfg("/getactivescheme"));
        return m.Success ? m.Value : null;
    }

    private static string KeyPath(string guid) =>
        $@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\{guid}\{SUB_PROCESSOR}\{CPMINCORES}";

    public bool IsApplied()
    {
        var g = ActiveScheme();
        if (g is null) return false;
        using var k = Registry.LocalMachine.OpenSubKey(KeyPath(g));
        return k?.GetValue("ACSettingIndex") is int v && v == 100;
    }

    public void Apply(BackupData backup)
    {
        var g = ActiveScheme() ?? throw new InvalidOperationException("Не удалось определить схему питания.");
        var slot = backup.For(Id);
        slot["scheme"] = g;
        using (var k = Registry.LocalMachine.CreateSubKey(KeyPath(g)))
        {
            slot["ac"] = (k.GetValue("ACSettingIndex") as int?)?.ToString();
            slot["dc"] = (k.GetValue("DCSettingIndex") as int?)?.ToString();
            k.SetValue("ACSettingIndex", 100, RegistryValueKind.DWord);
            k.SetValue("DCSettingIndex", 100, RegistryValueKind.DWord);
        }
        ProcessRunner.Powercfg($"-setactive {g}"); // применить изменения
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        string g = slot.GetValueOrDefault("scheme") ?? ActiveScheme() ?? "";
        if (g.Length > 0)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(KeyPath(g), writable: true))
            {
                if (k is not null)
                {
                    if (int.TryParse(slot.GetValueOrDefault("ac"), out int ac)) k.SetValue("ACSettingIndex", ac, RegistryValueKind.DWord);
                    if (int.TryParse(slot.GetValueOrDefault("dc"), out int dc)) k.SetValue("DCSettingIndex", dc, RegistryValueKind.DWord);
                }
            }
            ProcessRunner.Powercfg($"-setactive {g}");
        }
        backup.Remove(Id);
    }
}
