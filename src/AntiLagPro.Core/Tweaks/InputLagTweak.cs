using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Уменьшить задержку ввода: отключает акселерацию мыши («Повышенная точность
/// установки указателя»). Курсор двигается 1:1 — прицел предсказуемее.
/// Применяется сразу через SystemParametersInfo, перезаход не нужен.
/// </summary>
public sealed class InputLagTweak : ITweak
{
    public string Id => "input-lag-mouse";
    public string Name => "Уменьшить задержку ввода (мышь)";
    public string Description =>
        "Отключает акселерацию мыши: Windows перестаёт менять дистанцию курсора от скорости " +
        "движения руки, прицел становится предсказуемым 1:1. Стандартная рекомендация для шутеров.";
    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => false;

    private const string KeyPath = @"Control Panel\Mouse";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, int[] vparam, uint winIni);
    private const uint SPI_SETMOUSE = 0x0004;
    private const uint SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

    private static readonly int[] NoAccel = { 0, 0, 0 };

    public bool IsApplied()
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
        return (k?.GetValue("MouseSpeed") as string) == "0";
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using (var k = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
                       ?? Registry.CurrentUser.CreateSubKey(KeyPath))
        {
            slot["s"] = k.GetValue("MouseSpeed") as string;
            slot["t1"] = k.GetValue("MouseThreshold1") as string;
            slot["t2"] = k.GetValue("MouseThreshold2") as string;
            k.SetValue("MouseSpeed", "0", RegistryValueKind.String);
            k.SetValue("MouseThreshold1", "0", RegistryValueKind.String);
            k.SetValue("MouseThreshold2", "0", RegistryValueKind.String);
        }
        _ = SystemParametersInfo(SPI_SETMOUSE, 0, NoAccel, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        string s = slot.GetValueOrDefault("s") ?? "1";
        string t1 = slot.GetValueOrDefault("t1") ?? "6";
        string t2 = slot.GetValueOrDefault("t2") ?? "10";

        using (var k = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true))
        {
            if (k is not null)
            {
                k.SetValue("MouseSpeed", s, RegistryValueKind.String);
                k.SetValue("MouseThreshold1", t1, RegistryValueKind.String);
                k.SetValue("MouseThreshold2", t2, RegistryValueKind.String);
            }
        }

        if (!int.TryParse(t1, out int i1)) i1 = 6;
        if (!int.TryParse(t2, out int i2)) i2 = 10;
        _ = SystemParametersInfo(SPI_SETMOUSE, 0, new[] { i1, i2, s == "0" ? 0 : 1 },
                                 SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        backup.Remove(Id);
    }
}
