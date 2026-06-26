using System.Text.RegularExpressions;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Кастомная схема питания "AntiLag" (как делал оригинальный AntiLag:
/// дублирует активный план и правит КОПИЮ — твой исходный план не трогается).
///
/// Что отключаем (по результатам ресёрча для Win11 / Ryzen):
///   - Core parking (парковка ядер) -> убирает лаг от "разбудки" ядер на взрывных нагрузках
///   - Processor min/max state = 100%, boost = Aggressive
///   - PCI Express ASPM (энергосбережение шины) = Off
///   - Диск не выключать, политика охлаждения = Active
/// </summary>
public sealed class PowerPlanTweak : ITweak
{
    public string Id => "power-plan";
    public string Name => "Схема питания AntiLag";
    public string Description =>
        "Дубль активного плана: парковка ядер выключена, троттлинг убран " +
        "(процессор 100%, boost aggressive), PCIe-энергосбережение off. " +
        "Оригинальный план не трогается.";
    public TweakTier Tier => TweakTier.Universal;
    public bool RequiresReboot => false;

    private const string PlanName = "AntiLag";

    // --- GUID подгрупп и параметров powercfg ---
    private const string SUB_PROCESSOR  = "54533251-82be-4824-96c1-47b60b740d00";
    private const string PROCTHROTTLEMIN= "893dee8e-2bef-41e0-89c6-b55d0929964c";
    private const string PROCTHROTTLEMAX= "bc5038f7-23e0-4960-96da-33abaf5935ec";
    private const string CPMINCORES     = "0cc5b647-c1df-4637-891a-dec35c318583"; // мин. незапаркованных ядер, %
    private const string PERFBOOSTMODE  = "be337238-0d82-4146-a960-4f3749d470c7";
    private const string SYSCOOLPOL     = "94d3a615-a899-4ac5-ae2b-e4d8f634367f";
    private const string SUB_PCIEXPRESS = "501a4d13-42af-4429-9fd1-a8218c268e20";
    private const string ASPM           = "ee12f906-d277-404b-b6da-e5fa1a576df5";
    private const string SUB_DISK       = "0012ee47-9041-4b5d-9b77-535fba8b1442";
    private const string DISKIDLE       = "6738e2c4-e8a5-4a42-b16a-e040e769756e";

    private static readonly Regex GuidRx = new(
        @"[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}", RegexOptions.Compiled);

    public bool IsApplied()
    {
        // Активна ли наша схема? Смотрим имя в скобках в выводе powercfg.
        string active = ProcessRunner.Powercfg("/getactivescheme");
        return active.Contains(PlanName, StringComparison.OrdinalIgnoreCase);
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);

        // 1. Запомнить исходную активную схему (чтобы вернуть при откате).
        string activeGuid = ExtractGuid(ProcessRunner.Powercfg("/getactivescheme"))
            ?? throw new InvalidOperationException("Не удалось определить активную схему питания.");
        slot["originalActiveGuid"] = activeGuid;

        // 2. Дублировать её -> получаем новый GUID.
        string dupOut = ProcessRunner.Powercfg($"-duplicatescheme {activeGuid}");
        string newGuid = ExtractGuid(dupOut)
            ?? throw new InvalidOperationException("Не удалось создать копию схемы питания.");
        slot["createdGuid"] = newGuid;

        // 3. Переименовать и настроить копию.
        ProcessRunner.Powercfg($"-changename {newGuid} \"{PlanName}\" \"Created by AntiLag-v1\"");
        SetValue(newGuid, SUB_PROCESSOR, PROCTHROTTLEMIN, 100);
        SetValue(newGuid, SUB_PROCESSOR, PROCTHROTTLEMAX, 100);
        SetValue(newGuid, SUB_PROCESSOR, CPMINCORES, 100);   // 100% ядер активны = парковка off
        SetValue(newGuid, SUB_PROCESSOR, PERFBOOSTMODE, 2);  // 2 = Aggressive
        SetValue(newGuid, SUB_PROCESSOR, SYSCOOLPOL, 1);     // 1 = Active cooling
        SetValue(newGuid, SUB_PCIEXPRESS, ASPM, 0);          // 0 = Off
        SetValue(newGuid, SUB_DISK, DISKIDLE, 0);            // 0 = никогда не выключать

        // 4. Сделать активной.
        ProcessRunner.Powercfg($"-setactive {newGuid}");
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);

        if (slot.TryGetValue("originalActiveGuid", out var orig) && !string.IsNullOrEmpty(orig))
            ProcessRunner.Powercfg($"-setactive {orig}");

        if (slot.TryGetValue("createdGuid", out var created) && !string.IsNullOrEmpty(created))
            ProcessRunner.Powercfg($"-delete {created}");

        backup.Remove(Id);
    }

    // Ставим значение и для сети (AC), и для батареи (DC) — на случай ноутбука.
    private static void SetValue(string scheme, string sub, string setting, int value)
    {
        ProcessRunner.Powercfg($"-setacvalueindex {scheme} {sub} {setting} {value}");
        ProcessRunner.Powercfg($"-setdcvalueindex {scheme} {sub} {setting} {value}");
    }

    private static string? ExtractGuid(string text)
    {
        var m = GuidRx.Match(text);
        return m.Success ? m.Value : null;
    }
}
