using System.Globalization;

namespace AntiLagPro.Core;

/// <summary>
/// AgeLevel: 0 = актуален, 1 = устарел (&gt;2 лет), 2 = сильно устарел (&gt;4 лет), 3 = дата неизвестна.
/// </summary>
public sealed record DriverInfo(string Name, string Version, string DateStr, int AgeLevel, string? VendorUrl);

/// <summary>Доступное обновление драйвера из Windows Update.</summary>
public sealed class DriverUpdate
{
    public string Title { get; init; } = "";
    internal object ComUpdate { get; init; } = default!; // IUpdate (COM)
}

/// <summary>
/// Драйверы: инвентаризация (сторонние), ссылки на вендора и работа с Windows Update
/// (поиск + установка проверенных драйверов из каталога Microsoft).
/// </summary>
public static class DriverChecker
{
    public static List<DriverInfo> ScanDrivers()
    {
        string cmd =
            "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; " +
            "Get-CimInstance Win32_PnPSignedDriver | " +
            "Where-Object { $_.DeviceName -and $_.DeviceClass -in 'DISPLAY','NET','MEDIA','USB','MOUSE','KEYBOARD','BLUETOOTH' } | " +
            "ForEach-Object { $d = if ($_.DriverDate) { $_.DriverDate.ToString('yyyy-MM-dd') } else { '' }; " +
            "('{0}~{1}~{2}~{3}' -f $_.DeviceName, $_.DriverVersion, $d, $_.DriverProviderName) }";

        var (_, outp, _) = ProcessRunner.Run("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{cmd}\"");

        var seen = new HashSet<string>();
        var list = new List<DriverInfo>();
        foreach (var raw in outp.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            var p = line.Split('~');
            if (p.Length < 4) continue;

            string name = p[0].Trim();
            if (name.Length == 0 || !seen.Add(name)) continue;

            string ver = p[1].Trim();
            string date = p[2].Trim();
            string provider = p[3].Trim();

            if (provider.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0) continue; // inbox — пропускаем

            list.Add(new DriverInfo(name, ver, date, AgeLevel(date), VendorUrl(name + " " + provider)));
        }

        return list
            .OrderByDescending(d => d.AgeLevel == 3 ? 1 : d.AgeLevel)
            .ThenBy(d => d.Name)
            .ToList();
    }

    private static int AgeLevel(string date)
    {
        if (!DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return 3;
        double years = (DateTime.Now - dt).TotalDays / 365.0;
        if (years > 4) return 2;
        if (years > 2) return 1;
        return 0;
    }

    private static string? VendorUrl(string text)
    {
        text = text.ToLowerInvariant();
        if (text.Contains("nvidia"))   return "https://www.nvidia.com/Download/index.aspx";
        if (text.Contains("realtek"))  return "https://www.realtek.com/en/downloads";
        if (text.Contains("intel"))    return "https://www.intel.com/content/www/us/en/download-center/home.html";
        if (text.Contains("amd") || text.Contains("advanced micro devices") || text.Contains("ati "))
                                        return "https://www.amd.com/en/support";
        if (text.Contains("focusrite"))return "https://focusrite.com/downloads";
        if (text.Contains("logitech")) return "https://support.logi.com/hc/en-us/categories/360001759473";
        return null;
    }

    /// <summary>Поиск доступных обновлений драйверов в Windows Update (онлайн, может занять до минуты).</summary>
    public static List<DriverUpdate> GetDriverUpdates()
    {
        var list = new List<DriverUpdate>();
        Type? t = Type.GetTypeFromProgID("Microsoft.Update.Session");
        if (t is null) return list;

        dynamic session = Activator.CreateInstance(t)!;
        dynamic searcher = session.CreateUpdateSearcher();
        searcher.Online = true;
        dynamic res = searcher.Search("IsInstalled=0 and Type='Driver'");
        foreach (dynamic u in res.Updates)
            list.Add(new DriverUpdate { Title = (string)u.Title, ComUpdate = u });
        return list;
    }

    /// <summary>Скачать и установить выбранные обновления через Windows Update. Возвращает итог.</summary>
    public static string InstallUpdates(IEnumerable<DriverUpdate> updates, IProgress<string> log)
    {
        var items = updates.ToList();
        if (items.Count == 0) return "Нечего устанавливать.";

        Type? sT = Type.GetTypeFromProgID("Microsoft.Update.Session");
        Type? cT = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl");
        if (sT is null || cT is null) return "Windows Update недоступен.";

        dynamic session = Activator.CreateInstance(sT)!;
        dynamic coll = Activator.CreateInstance(cT)!;
        foreach (var u in items)
        {
            try { ((dynamic)u.ComUpdate).AcceptEula(); } catch { }
            coll.Add(u.ComUpdate);
        }

        log.Report("Скачивание драйверов…");
        dynamic downloader = session.CreateUpdateDownloader();
        downloader.Updates = coll;
        downloader.Download();

        log.Report("Установка…");
        dynamic installer = session.CreateUpdateInstaller();
        installer.Updates = coll;
        dynamic result = installer.Install();

        int code = (int)result.ResultCode;          // 2 = Succeeded, 3 = SucceededWithErrors
        bool reboot = (bool)result.RebootRequired;
        string ok = code is 2 or 3 ? "Установлено." : $"Не удалось (код {code}).";
        return ok + (reboot ? " Нужна перезагрузка." : "");
    }
}
