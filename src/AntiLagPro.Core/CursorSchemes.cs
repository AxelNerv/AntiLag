using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AntiLagPro.Core;

public sealed record CursorScheme(string Id, string Name, string Author, string Folder, bool BuiltIn);

/// <summary>
/// Схемы курсоров: ставятся одним кликом вместо ручного «ПКМ по Install.inf».
/// Встроенные паки распаковываются в %ProgramData%, свои — просто кладутся
/// туда же папкой (нужен Install.inf внутри) и подхватываются автоматически.
/// </summary>
public static class CursorSchemes
{
    public static readonly string Root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                     "AntiLagPro", "Cursors");

    private const string RegPath = @"Control Panel\Cursors";

    /// <summary>Роли курсоров в реестре (порядок как в системной схеме).</summary>
    private static readonly string[] Roles =
    {
        "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No",
        "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Person", "Pin"
    };

    // Встроенные паки: ресурс -> (название, автор)
    private static readonly (string Id, string Name, string Author)[] BuiltIn =
    {
        ("dot-md",            "DOT MD",           "alexgal23"),
        ("dot-ml",            "DOT ML",           "alexgal23"),
        ("pointer-black",     "Point.er Black",   "Point.er"),
        ("pointer-blackplus", "Point.er Black +", "Point.er"),
        ("pointer-white",     "Point.er White",   "Point.er"),
        ("pointer-whiteplus", "Point.er White +", "Point.er"),
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint uParam, IntPtr pv, uint winIni);
    private const uint SPI_SETCURSORS = 0x0057;
    private const uint SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

    /// <summary>Все доступные схемы: встроенные + добавленные пользователем.</summary>
    public static IReadOnlyList<CursorScheme> All()
    {
        EnsureExtracted();
        var list = new List<CursorScheme>();

        foreach (var (id, name, author) in BuiltIn)
        {
            string dir = Path.Combine(Root, id);
            if (Directory.Exists(dir)) list.Add(new CursorScheme(id, name, author, dir, true));
        }

        try
        {
            foreach (var dir in Directory.GetDirectories(Root))
            {
                string id = Path.GetFileName(dir);
                if (BuiltIn.Any(b => b.Id == id)) continue;                  // уже добавлен
                if (!File.Exists(Path.Combine(dir, "Install.inf"))) continue; // не пак курсоров
                list.Add(new CursorScheme(id, id, "свой пак", dir, false));
            }
        }
        catch { }

        return list;
    }

    /// <summary>Название текущей схемы (пусто = системная).</summary>
    public static string Current
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RegPath);
            return k?.GetValue("") as string ?? "";
        }
    }

    private static void EnsureExtracted()
    {
        Directory.CreateDirectory(Root);
        var asm = typeof(CursorSchemes).Assembly;

        foreach (var (id, _, _) in BuiltIn)
        {
            string dir = Path.Combine(Root, id);
            if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "Install.inf"))) continue;
            try
            {
                using var rs = asm.GetManifestResourceStream($"AntiLagPro.Core.cursors.{id}.zip");
                if (rs is null) continue;
                Directory.CreateDirectory(dir);
                using var zip = new ZipArchive(rs, ZipArchiveMode.Read);
                zip.ExtractToDirectory(dir, overwriteFiles: true);
            }
            catch { /* не критично — схема просто не появится */ }
        }
    }

    /// <summary>
    /// Читает Install.inf и возвращает «роль реестра -> файл курсора».
    /// [Wreg] задаёт роли через %переменные%, [Strings] — их значения.
    /// </summary>
    private static Dictionary<string, string> ParseInf(string infPath)
    {
        var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rawRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // роль -> имя переменной
        string section = "";

        // ВАЖНО: [Strings] в .inf обычно идёт ПОСЛЕ [Wreg], поэтому сначала
        // собираем обе секции целиком и только потом подставляем значения.
        foreach (var raw in File.ReadAllLines(infPath))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[')) { section = line.Trim('[', ']'); continue; }

            if (section.Equals("Strings", StringComparison.OrdinalIgnoreCase))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                strings[line[..eq].Trim()] = line[(eq + 1)..].Trim().Trim('"');
            }
            else if (section.Equals("Wreg", StringComparison.OrdinalIgnoreCase))
            {
                // HKCU,"Control Panel\Cursors",Arrow,0x00020000,"%10%\%CUR_DIR%\%pointer%"
                var parts = line.Split(',');
                if (parts.Length < 5) continue;
                string role = parts[2].Trim();
                if (role.Length == 0) continue;

                var m = Regex.Matches(parts[^1].Trim().Trim('"'), @"%([^%]+)%");
                if (m.Count == 0) continue;
                rawRoles[role] = m[^1].Groups[1].Value;
            }
        }

        var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (role, varName) in rawRoles)
        {
            if (strings.TryGetValue(varName, out var file)) roles[role] = file;
            else if (varName.Contains('.')) roles[role] = varName;   // в файле прямое имя, без переменной
        }
        return roles;
    }

    /// <summary>Ставит схему и применяет её сразу, без перезагрузки.</summary>
    public static void Apply(CursorScheme scheme, BackupData backup)
    {
        var roles = ParseInf(Path.Combine(scheme.Folder, "Install.inf"));
        if (roles.Count == 0) throw new InvalidOperationException("В паке нет Install.inf с описанием курсоров.");

        var slot = backup.For("cursor-scheme");
        using var k = Registry.CurrentUser.OpenSubKey(RegPath, writable: true)
                      ?? Registry.CurrentUser.CreateSubKey(RegPath);

        // сохраняем прежнее состояние один раз
        if (slot.Count == 0)
        {
            slot["(Default)"] = k.GetValue("") as string ?? "";
            foreach (var r in Roles) slot[r] = k.GetValue(r) as string;
        }

        k.SetValue("", scheme.Name, RegistryValueKind.String);
        foreach (var r in Roles)
        {
            if (roles.TryGetValue(r, out var file))
                k.SetValue(r, Path.Combine(scheme.Folder, file), RegistryValueKind.String);
            else
                k.SetValue(r, "", RegistryValueKind.String);   // роли нет в паке — пусто, как в системных схемах
        }

        // добавляем схему в системный список (Панель управления -> Мышь)
        try
        {
            using var sk = Registry.CurrentUser.CreateSubKey(RegPath + @"\Schemes");
            sk.SetValue(scheme.Name, string.Join(",", Roles.Select(r =>
                roles.TryGetValue(r, out var f) ? Path.Combine(scheme.Folder, f) : "")), RegistryValueKind.ExpandString);
        }
        catch { }

        SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }

    /// <summary>Возвращает курсоры Windows по умолчанию.</summary>
    public static void RestoreSystem(BackupData backup)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RegPath, writable: true);
        if (k is null) return;

        if (backup.Has("cursor-scheme"))
        {
            var slot = backup.For("cursor-scheme");
            k.SetValue("", slot.GetValueOrDefault("(Default)") ?? "", RegistryValueKind.String);
            foreach (var r in Roles)
                k.SetValue(r, slot.GetValueOrDefault(r) ?? "", RegistryValueKind.String);
            backup.Remove("cursor-scheme");
        }
        else
        {
            // бэкапа нет — просто чистим на системные
            k.SetValue("", "", RegistryValueKind.String);
            foreach (var r in Roles) k.SetValue(r, "", RegistryValueKind.String);
        }

        SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }
}
