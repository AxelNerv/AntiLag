using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Уменьшение буфера событий мыши/клавиатуры: MouseDataQueueSize и
/// KeyboardDataQueueSize со стандартных 100 до 50. Меньший буфер = события
/// доходят до игры чуть быстрее. 50 — безопасный минимум (ниже 20 бывают
/// пропуски ввода). Нужна перезагрузка.
/// </summary>
public sealed class InputQueueTweak : ITweak
{
    public string Id => "input-queue";
    public string Name => "Уменьшить буфер мыши и клавиатуры";
    public string Description =>
        "Сокращает очередь событий мыши/клавиатуры в драйвере (100 → 50) — ввод доходит " +
        "до игры чуть быстрее. Эффект небольшой, но безопасный: 50 достаточно даже для " +
        "мышей 8000 Гц. Вступает в силу после перезагрузки.";
    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => true;

    private const string MouseKey = @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters";
    private const string KbdKey   = @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters";
    private const int Target = 50;

    public bool IsApplied()
    {
        using var m = Registry.LocalMachine.OpenSubKey(MouseKey);
        using var k = Registry.LocalMachine.OpenSubKey(KbdKey);
        return m?.GetValue("MouseDataQueueSize") is int mv && mv == Target
            && k?.GetValue("KeyboardDataQueueSize") is int kv && kv == Target;
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using (var m = Registry.LocalMachine.OpenSubKey(MouseKey, writable: true)
                       ?? Registry.LocalMachine.CreateSubKey(MouseKey))
        {
            slot["mouse"] = (m.GetValue("MouseDataQueueSize") as int?)?.ToString();
            m.SetValue("MouseDataQueueSize", Target, RegistryValueKind.DWord);
        }
        using (var k = Registry.LocalMachine.OpenSubKey(KbdKey, writable: true)
                       ?? Registry.LocalMachine.CreateSubKey(KbdKey))
        {
            slot["kbd"] = (k.GetValue("KeyboardDataQueueSize") as int?)?.ToString();
            k.SetValue("KeyboardDataQueueSize", Target, RegistryValueKind.DWord);
        }
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);

        using (var m = Registry.LocalMachine.OpenSubKey(MouseKey, writable: true))
        {
            if (m is not null)
            {
                if (int.TryParse(slot.GetValueOrDefault("mouse"), out int mv))
                    m.SetValue("MouseDataQueueSize", mv, RegistryValueKind.DWord);
                else m.DeleteValue("MouseDataQueueSize", throwOnMissingValue: false); // не было = дефолт 100
            }
        }
        using (var k = Registry.LocalMachine.OpenSubKey(KbdKey, writable: true))
        {
            if (k is not null)
            {
                if (int.TryParse(slot.GetValueOrDefault("kbd"), out int kv))
                    k.SetValue("KeyboardDataQueueSize", kv, RegistryValueKind.DWord);
                else k.DeleteValue("KeyboardDataQueueSize", throwOnMissingValue: false);
            }
        }
        backup.Remove(Id);
    }
}
