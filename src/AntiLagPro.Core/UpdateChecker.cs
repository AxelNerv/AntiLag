using System.Net.Http;
using System.Text.Json;

namespace AntiLagPro.Core;

public sealed record UpdateInfo(Version Latest, string Url);

/// <summary>
/// Проверка обновлений через GitHub Releases API. Молчит при любой ошибке
/// (нет сети / GitHub заблокирован) — обновление не критично.
/// </summary>
public static class UpdateChecker
{
    private const string Api = "https://api.github.com/repos/AxelNerv/AntiLag/releases/latest";

    public static async Task<UpdateInfo?> Check(Version current)
    {
        try
        {
            using var http = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AntiLag-UpdateCheck"); // GitHub API требует UA
            using var doc = JsonDocument.Parse(await http.GetStringAsync(Api));

            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            string url = doc.RootElement.GetProperty("html_url").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;

            return latest > current ? new UpdateInfo(latest, url) : null;
        }
        catch { return null; }
    }
}
