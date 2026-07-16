namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Отключение «динамического тика» (bcdedit disabledynamictick yes).
/// Dynamic tick — энергосберегающий режим таймера ядра (важен для ноутбуков),
/// на десктопе может давать неровный тик → микро-джиттер. Обратимо
/// (/deletevalue), вступает в силу после перезагрузки.
/// </summary>
public sealed class DynamicTickTweak : ITweak
{
    public string Id => "dynamic-tick";
    public string Name => "Отключить динамический тик";
    public string Description =>
        "Динамический тик — энергосбережение таймера ядра (нужно ноутбукам). На игровом " +
        "десктопе его отключение даёт более ровный системный тик — меньше микро-джиттера. " +
        "Вступает в силу после перезагрузки.";
    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => true;

    public bool IsApplied()
    {
        var (code, outp, _) = ProcessRunner.Run("bcdedit.exe", "/enum {current}");
        if (code != 0) return false;
        foreach (var raw in outp.Split('\n'))
        {
            var l = raw.Trim();
            if (!l.StartsWith("disabledynamictick", StringComparison.OrdinalIgnoreCase)) continue;
            // значение печатается локализованно: Yes/Да/True
            return l.Contains("yes", StringComparison.OrdinalIgnoreCase)
                || l.Contains("да", StringComparison.OrdinalIgnoreCase)
                || l.Contains("true", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        slot["was"] = IsApplied() ? "on" : "off";
        ProcessRunner.Run("bcdedit.exe", "/set {current} disabledynamictick yes");
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        if (slot.GetValueOrDefault("was") != "on")
            ProcessRunner.Run("bcdedit.exe", "/deletevalue {current} disabledynamictick");
        backup.Remove(Id);
    }
}
