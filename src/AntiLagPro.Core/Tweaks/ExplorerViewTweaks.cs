using System.Globalization;
using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>Общая база для твиков внешнего вида: одно значение в реестре.</summary>
public abstract class RegValueTweak : ITweak
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public TweakTier Tier => TweakTier.Appearance;
    public virtual bool RequiresReboot => false;

    protected abstract RegistryKey Hive { get; }
    protected abstract string KeyPath { get; }
    protected abstract string ValueName { get; }
    protected abstract int OnValue { get; }

    public bool IsApplied()
    {
        using var k = Hive.OpenSubKey(KeyPath);
        return k?.GetValue(ValueName) is int v && v == OnValue;
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using var k = Hive.OpenSubKey(KeyPath, writable: true) ?? Hive.CreateSubKey(KeyPath);
        slot["v"] = (k.GetValue(ValueName) as int?)?.ToString(CultureInfo.InvariantCulture);
        k.SetValue(ValueName, OnValue, RegistryValueKind.DWord);
        AfterChange();
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        using (var k = Hive.OpenSubKey(KeyPath, writable: true))
        {
            if (k is not null)
            {
                if (int.TryParse(slot.GetValueOrDefault("v"), out int v)) k.SetValue(ValueName, v, RegistryValueKind.DWord);
                else k.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        backup.Remove(Id);
        AfterChange();
    }

    /// <summary>Перезапуск проводника, если изменение того требует.</summary>
    protected virtual void AfterChange() { }

    protected static void RestartExplorer()
    {
        try { ProcessRunner.Run("taskkill.exe", "/F /IM explorer.exe"); } catch { }
        try { System.Diagnostics.Process.Start("explorer.exe"); } catch { }
    }
}

/// <summary>Убрать «Галерею» из панели навигации проводника.</summary>
public sealed class ExplorerGalleryTweak : RegValueTweak
{
    public override string Id => "explorer-gallery";
    public override string Name => "Убрать «Галерею» из проводника";
    public override string Description =>
        "Скрывает раздел «Галерея» в левой панели проводника — он появился в Windows 11 и " +
        "многим только мешает. Настройка личная (для текущего пользователя).";

    protected override RegistryKey Hive => Registry.CurrentUser;
    protected override string KeyPath => @"Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}";
    protected override string ValueName => "System.IsPinnedToNameSpaceTree";
    protected override int OnValue => 0;
    protected override void AfterChange() => RestartExplorer();
}

/// <summary>Убрать «Главную» из панели навигации проводника.</summary>
public sealed class ExplorerHomeTweak : RegValueTweak
{
    public override string Id => "explorer-home";
    public override string Name => "Убрать «Главную» из проводника";
    public override string Description =>
        "Скрывает раздел «Главная» (Home) в левой панели проводника — вместо него сразу " +
        "виден «Этот компьютер». Требует прав администратора.";

    protected override RegistryKey Hive => Registry.LocalMachine;
    protected override string KeyPath => @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";
    protected override string ValueName => "HubMode";
    protected override int OnValue => 1;
    protected override void AfterChange() => RestartExplorer();
}

/// <summary>Показывать расширения файлов.</summary>
public sealed class FileExtensionsTweak : RegValueTweak
{
    public override string Id => "file-extensions";
    public override string Name => "Показывать расширения файлов";
    public override string Description =>
        "Windows по умолчанию прячет расширения (.exe, .txt) — из-за этого проще нарваться " +
        "на файл-подделку вроде «фото.jpg.exe». Включает показ расширений у всех файлов.";

    protected override RegistryKey Hive => Registry.CurrentUser;
    protected override string KeyPath => @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    protected override string ValueName => "HideFileExt";
    protected override int OnValue => 0;
    protected override void AfterChange() => RestartExplorer();
}

/// <summary>Классическое контекстное меню (как в Windows 10).</summary>
public sealed class ClassicContextMenuTweak : ITweak
{
    public string Id => "classic-context-menu";
    public string Name => "Классическое меню правой кнопки";
    public string Description =>
        "Возвращает полное контекстное меню как в Windows 10 — без «Показать дополнительные " +
        "параметры». Все пункты программ сразу в списке.";
    public TweakTier Tier => TweakTier.Appearance;
    public bool RequiresReboot => false;

    private const string KeyPath =
        @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

    public bool IsApplied()
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
        return k is not null && k.GetValue("") as string == "";
    }

    public void Apply(BackupData backup)
    {
        backup.For(Id)["on"] = "1";
        using (var k = Registry.CurrentUser.CreateSubKey(KeyPath))
            k.SetValue("", "", RegistryValueKind.String);   // пустая строка отключает новое меню
        RestartExplorer();
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", throwOnMissingSubKey: false);
        }
        catch { }
        backup.Remove(Id);
        RestartExplorer();
    }

    private static void RestartExplorer()
    {
        try { ProcessRunner.Run("taskkill.exe", "/F /IM explorer.exe"); } catch { }
        try { System.Diagnostics.Process.Start("explorer.exe"); } catch { }
    }
}
