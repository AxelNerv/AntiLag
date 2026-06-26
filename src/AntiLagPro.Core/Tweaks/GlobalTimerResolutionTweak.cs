using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// ГЛАВНЫЙ фикс под Windows 11 (24H2+).
/// Ставит GlobalTimerResolutionRequests=1, чтобы запрос Timer Resolution 0.5 ms
/// действовал на ВСЮ систему, а не только на процесс, который его запросил.
/// Без этого современные тулзы (AntiLag/ExitLag) на играх не работают.
/// Требует перезагрузки.
/// </summary>
public sealed class GlobalTimerResolutionTweak : ITweak
{
    public string Id => "global-timer-resolution";
    public string Name => "Глобальный Timer Resolution (флаг Win11)";
    public string Description =>
        "Делает так, чтобы таймер 0.5 ms действовал на всю систему. " +
        "Это причина, по которой старые тулзы перестали помогать. Требует перезагрузки.";
    public TweakTier Tier => TweakTier.Universal;
    public bool RequiresReboot => true;

    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
    private const string ValueName = "GlobalTimerResolutionRequests";

    public bool IsApplied()
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is int v && v == 1;
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true)
            ?? throw new InvalidOperationException("Не открылся ключ kernel (нужны права администратора).");

        // Сохраняем оригинал: либо старое DWORD, либо метку "отсутствовало".
        object? original = key.GetValue(ValueName);
        slot["existed"] = (original is not null).ToString();
        slot["value"] = original?.ToString();

        key.SetValue(ValueName, 1, RegistryValueKind.DWord);
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
        if (key is null) return;

        bool existed = slot.TryGetValue("existed", out var e) && e == "True";
        if (existed && int.TryParse(slot.GetValueOrDefault("value"), out int old))
            key.SetValue(ValueName, old, RegistryValueKind.DWord);   // вернуть старое значение
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);  // значения не было — удалить

        backup.Remove(Id);
    }
}
