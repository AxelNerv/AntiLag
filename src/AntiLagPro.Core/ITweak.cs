namespace AntiLagPro.Core;

/// <summary>К какому уровню относится твик (для группировки в UI).</summary>
public enum TweakTier
{
    /// Базовое: универсально и безопасно на любом железе.
    Universal,
    /// Игровые оптимизации (опционально) — по умолчанию ВЫКЛ, пользователь решает сам.
    Game,
    /// Зависит от конкретного железа.
    HardwareSpecific
}

/// <summary>
/// Один обратимый твик системы. Контракт прост:
///  - Apply() ОБЯЗАН сначала сохранить оригинал в backup, потом менять.
///  - Restore() возвращает всё назад из backup.
/// </summary>
public interface ITweak
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    TweakTier Tier { get; }

    /// Применён ли твик прямо сейчас (для состояния галочки).
    bool IsApplied();

    /// Требуется ли перезагрузка, чтобы изменение вступило в силу.
    bool RequiresReboot { get; }

    void Apply(BackupData backup);
    void Restore(BackupData backup);
}
