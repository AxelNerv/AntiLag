using System.Globalization;
using System.IO;
using System.Text;

namespace AntiLagPro.Core;

/// <summary>
/// Журнал программы. Нужен там, где ошибку нельзя показывать пользователю
/// (окно закрылось во время обхода, файл занят), но и молча терять нельзя —
/// иначе поломка выглядит как «просто не работает».
/// </summary>
public static class Log
{
    private static readonly object Lock = new();
    private const long MaxBytes = 512 * 1024;

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "AntiLagPro", "log.txt");

    public static void Info(string message) => Write("INFO ", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN ", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                Rotate();

                var sb = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                    .Append(' ').Append(level).Append(' ').Append(message);
                if (ex is not null) sb.Append(" — ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);

                File.AppendAllText(Path, sb.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch (Exception logEx)
        {
            // Журнал не должен ронять программу: пишем хотя бы в отладчик.
            System.Diagnostics.Debug.WriteLine("Не удалось записать журнал: " + logEx.Message);
        }
    }

    /// <summary>Обрезает файл, когда он разрастается, — храним только свежую половину.</summary>
    private static void Rotate()
    {
        var f = new FileInfo(Path);
        if (!f.Exists || f.Length < MaxBytes) return;

        var lines = File.ReadAllLines(Path);
        File.WriteAllLines(Path, lines.Skip(lines.Length / 2), Encoding.UTF8);
    }
}
