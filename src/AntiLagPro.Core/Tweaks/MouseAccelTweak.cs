using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Отключение ускорения мыши («Повышенная точность установки указателя»).
/// Windows меняет дистанцию курсора в зависимости от СКОРОСТИ движения — это
/// ломает мышечную память в шутерах. Off = сырой 1:1 ввод. Применяется сразу
/// (SystemParametersInfo), реестр — чтобы пережило перезагрузку.
/// </summary>
public sealed class MouseAccelTweak : ITweak
{
    public string Id => "mouse-accel";
    public string Name => "Отключить ускорение мыши";
    public string Description =>
        "Выключает «Повышенную точность указателя» — Windows перестаёт менять дистанцию " +
        "курсора от скорости движения руки. Прицел становится предсказуемым 1:1 " +
        "(мышечная память). Стандартная рекомендация для шутеров.";
    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => false;

    private const string KeyPath = @"Control Panel\Mouse";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, int[] vparam, uint winIni);
    private const uint SPI_SETMOUSE = 0x0004;
    private const uint SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

    public bool IsApplied()
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
        return k?.GetValue("MouseSpeed") as string == "0";
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(KeyPath);

        slot["speed"] = k.GetValue("MouseSpeed") as string;
        slot["t1"]    = k.GetValue("MouseThreshold1") as string;
        slot["t2"]    = k.GetValue("MouseThreshold2") as string;

        k.SetValue("MouseSpeed", "0", RegistryValueKind.String);
        k.SetValue("MouseThreshold1", "0", RegistryValueKind.String);
        k.SetValue("MouseThreshold2", "0", RegistryValueKind.String);

        SystemParametersInfo(SPI_SETMOUSE, 0, new[] { 0, 0, 0 }, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);

        string speed = slot.GetValueOrDefault("speed") ?? "1";
        string t1    = slot.GetValueOrDefault("t1") ?? "6";
        string t2    = slot.GetValueOrDefault("t2") ?? "10";

        if (k is not null)
        {
            k.SetValue("MouseSpeed", speed, RegistryValueKind.String);
            k.SetValue("MouseThreshold1", t1, RegistryValueKind.String);
            k.SetValue("MouseThreshold2", t2, RegistryValueKind.String);
        }

        int.TryParse(t1, out int i1); int.TryParse(t2, out int i2);
        SystemParametersInfo(SPI_SETMOUSE, 0, new[] { i1, i2, speed == "0" ? 0 : 1 },
            SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        backup.Remove(Id);
    }
}
