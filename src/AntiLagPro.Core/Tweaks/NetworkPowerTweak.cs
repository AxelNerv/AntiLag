using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AntiLagPro.Core.Tweaks;

/// <summary>
/// Отключить энергосбережение сетевых адаптеров («разрешить Windows отключать
/// устройство для экономии»). Реестр: PnPCapabilities = 24 (бит 0x10 запрещает усыпление).
/// Применяется ко всем физическим адаптерам. Полный эффект — после перезагрузки.
/// НЕ выключает адаптер, только запрещает его усыплять.
/// </summary>
public sealed class NetworkPowerTweak : ITweak
{
    public string Id => "nic-power";
    public string Name => "Отключить энергосбережение сети";
    public string Description =>
        "Запрещает Windows усыплять сетевые адаптеры ради экономии — стабильнее " +
        "проводной интернет, меньше DPC-спайков. Интернет не отключается.";
    public TweakTier Tier => TweakTier.Game;
    public bool RequiresReboot => false;

    private const string ClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    private static IEnumerable<string> NicSubkeys(RegistryKey root)
        => root.GetSubKeyNames().Where(n => Regex.IsMatch(n, @"^\d{4}$"));

    /// <summary>
    /// Только ФИЗИЧЕСКИЕ адаптеры (бит NCF_PHYSICAL = 0x4 в Characteristics).
    /// Виртуальные (WAN Miniport, Hyper-V, Radmin VPN) исключаем: Hyper-V-адаптер
    /// пересоздаётся при загрузке и сбрасывает PnPCapabilities, из-за чего тумблер
    /// ложно показывал «выкл» после перезапуска.
    /// </summary>
    private static bool IsPhysical(RegistryKey? k)
        => k?.GetValue("NetCfgInstanceId") is not null
           && k.GetValue("Characteristics") is int c && (c & 0x4) != 0;

    public bool IsApplied()
    {
        using var root = Registry.LocalMachine.OpenSubKey(ClassKey);
        if (root is null) return false;
        bool any = false;
        foreach (var sub in NicSubkeys(root))
        {
            using var k = root.OpenSubKey(sub);
            if (!IsPhysical(k)) continue; // только физические адаптеры
            any = true;
            int pnp = k!.GetValue("PnPCapabilities") is int v ? v : 0;
            if ((pnp & 0x10) == 0) return false; // хоть один ещё разрешает усыпление
        }
        return any;
    }

    public void Apply(BackupData backup)
    {
        var slot = backup.For(Id);
        using var root = Registry.LocalMachine.OpenSubKey(ClassKey, writable: true);
        if (root is null) return;

        foreach (var sub in NicSubkeys(root))
        {
            using var k = root.OpenSubKey(sub, writable: true);
            if (!IsPhysical(k)) continue;
            object? old = k!.GetValue("PnPCapabilities");
            slot[sub] = old is null ? "none" : old.ToString();
            k.SetValue("PnPCapabilities", 24, RegistryValueKind.DWord); // 0x18
        }
    }

    public void Restore(BackupData backup)
    {
        if (!backup.Has(Id)) return;
        var slot = backup.For(Id);
        using var root = Registry.LocalMachine.OpenSubKey(ClassKey, writable: true);
        if (root is not null)
        {
            foreach (var kv in slot)
            {
                using var k = root.OpenSubKey(kv.Key, writable: true);
                if (k is null) continue;
                if (kv.Value == "none")
                    k.DeleteValue("PnPCapabilities", throwOnMissingValue: false);
                else if (int.TryParse(kv.Value, out int v))
                    k.SetValue("PnPCapabilities", v, RegistryValueKind.DWord);
            }
        }
        backup.Remove(Id);
    }
}
