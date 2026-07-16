using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Игровой режим Windows (Game Mode): система отдаёт активной игре приоритет
/// CPU/GPU и откладывает фоновые задачи (обновления, индексация). В Win11
/// включён по умолчанию, но «оптимизаторы» и сборки часто его вырубают —
/// твик гарантирует, что он ВКЛ.
/// </summary>
public sealed class GameModeTweak : ITweak
{
    public string Id => "game-mode";
    public string Name => "Игровой режим Windows (Game Mode)";
    public string Description =>
        "Гарантирует, что Game Mode включён: активная игра получает приоритет CPU/GPU, " +
        "Windows откладывает фоновые задачи и обновления на время игры. Часто отключён " +
        "после «оптимизаторов» и кастомных сборок.";
    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => false;

    private const string KeyPath = @"Software\Microsoft\GameBar";

    public bool IsApplied()
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
        // отсутствие значения = включён по умолчанию
        return k?.GetValue("AutoGameModeEnabled") is not int v || v == 1;
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(KeyPath);
        slot["auto"] = (k.GetValue("AutoGameModeEnabled") as int?)?.ToString();
        k.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        if (k is not null)
        {
            if (int.TryParse(slot.GetValueOrDefault("auto"), out int v))
                k.SetValue("AutoGameModeEnabled", v, RegistryValueKind.DWord);
            else
                k.DeleteValue("AutoGameModeEnabled", throwOnMissingValue: false);
        }
        backup.Remove(Id);
    }
}
