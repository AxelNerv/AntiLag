using System.Text.Json;

namespace AntiLagPro.Core;

/// <summary>
/// Снимок оригинальных значений ДО применения твиков.
/// Хранится в %ProgramData%\AntiLagPro\backup.json, чтобы откат работал
/// даже после перезагрузки или закрытия программы.
/// </summary>
public sealed class BackupData
{
    /// Ключ = Id твика, значение = словарь "имя поля -> старое значение".
    public Dictionary<string, Dictionary<string, string?>> Tweaks { get; set; } = new();

    /// Получить (или создать) ячейку бэкапа для конкретного твика.
    public Dictionary<string, string?> For(string tweakId)
    {
        if (!Tweaks.TryGetValue(tweakId, out var dict))
        {
            dict = new Dictionary<string, string?>();
            Tweaks[tweakId] = dict;
        }
        return dict;
    }

    public bool Has(string tweakId) => Tweaks.ContainsKey(tweakId);
    public void Remove(string tweakId) => Tweaks.Remove(tweakId);
}

public static class BackupStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AntiLagPro");

    private static readonly string FilePath = Path.Combine(Dir, "backup.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static BackupData Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<BackupData>(File.ReadAllText(FilePath)) ?? new BackupData();
        }
        catch { /* битый файл -> начинаем с чистого */ }
        return new BackupData();
    }

    public static void Save(BackupData data)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(data, JsonOpts));
    }

    public static string Location => FilePath;
}
