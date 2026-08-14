using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace AntiLagPro.Core;

public sealed record UpdateInfo(Version Latest, string PageUrl, string? AssetUrl);

/// <summary>
/// Обновление через GitHub Releases: проверка, загрузка нового exe и подмена
/// на месте. Молчит при любой ошибке (нет сети / GitHub недоступен) —
/// обновление не должно мешать работе программы.
/// </summary>
public static class UpdateChecker
{
    private const string Api = "https://api.github.com/repos/AxelNerv/AntiLag/releases/latest";

    private static HttpClient MakeClient(TimeSpan timeout)
    {
        var h = new HttpClient { Timeout = timeout };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("AntiLag-Updater"); // GitHub API требует User-Agent
        return h;
    }

    public static async Task<UpdateInfo?> Check(Version current)
    {
        try
        {
            using var http = MakeClient(TimeSpan.FromSeconds(10));
            using var doc = JsonDocument.Parse(await http.GetStringAsync(Api));
            var root = doc.RootElement;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            string page = root.GetProperty("html_url").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || latest <= current) return null;

            // ищем приложенный exe
            string? asset = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                {
                    string name = a.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        asset = a.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

            return new UpdateInfo(latest, page, asset);
        }
        catch { return null; }
    }

    /// <summary>Качает новый exe во временный файл. Возвращает путь или null.</summary>
    public static async Task<string?> Download(UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(info.AssetUrl)) return null;
        string tmp = Path.Combine(Path.GetTempPath(), $"AntiLag_{info.Latest.ToString(3)}.exe");
        try
        {
            using var http = MakeClient(TimeSpan.FromMinutes(10));
            using var resp = await http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? -1;
            long read = 0;
            var buf = new byte[131072];

            using (var src = await resp.Content.ReadAsStreamAsync(ct))
            using (var dst = File.Create(tmp))
            {
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    read += n;
                    if (total > 0) progress?.Report(read * 100.0 / total);
                }
            }

            if (new FileInfo(tmp).Length < 1_000_000) { File.Delete(tmp); return null; } // подозрительно мало
            return tmp;
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return null;
        }
    }

    /// <summary>
    /// Ставит скачанный exe на место текущего и перезапускает программу.
    /// Запущенный файл перезаписать нельзя, но ПЕРЕИМЕНОВАТЬ Windows разрешает —
    /// на этом и держится подмена. Старый файл удалится при следующем старте.
    /// </summary>
    public static bool ApplyAndRestart(string newExe)
    {
        string cur = Environment.ProcessPath ?? "";
        if (string.IsNullOrEmpty(cur) || !File.Exists(newExe)) return false;

        string old = Path.Combine(Path.GetDirectoryName(cur)!,
                                  Path.GetFileNameWithoutExtension(cur) + ".old.exe");
        bool moved = false;
        try
        {
            try { if (File.Exists(old)) File.Delete(old); } catch { }

            File.Move(cur, old);      // освобождаем имя
            moved = true;
            File.Copy(newExe, cur);   // ставим новый на его место
            try { File.Delete(newExe); } catch { }

            Process.Start(new ProcessStartInfo(cur) { UseShellExecute = true });
            return true;
        }
        catch
        {
            if (moved && !File.Exists(cur)) { try { File.Move(old, cur); } catch { } }  // откат
            return false;
        }
    }

    /// <summary>Удаляет файл прошлой версии, оставшийся после обновления.</summary>
    public static void CleanupOld()
    {
        try
        {
            string cur = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(cur)) return;
            string old = Path.Combine(Path.GetDirectoryName(cur)!,
                                      Path.GetFileNameWithoutExtension(cur) + ".old.exe");
            if (File.Exists(old)) File.Delete(old);
        }
        catch { /* занят — удалится в следующий раз */ }
    }
}
