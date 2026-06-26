using System.Diagnostics;
using System.Text;

namespace AntiLagPro.Core;

/// <summary>Запуск консольных утилит (powercfg и т.п.) с захватом вывода.</summary>
internal static class ProcessRunner
{
    public static (int exitCode, string stdout, string stderr) Run(string exe, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Не удалось запустить {exe}");
        string outp = p.StandardOutput.ReadToEnd();
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, outp, err);
    }

    /// <summary>powercfg отдаёт текст на языке системы; GUID вытаскиваем регуляркой.</summary>
    public static string Powercfg(string args)
    {
        var (_, outp, _) = Run("powercfg.exe", args);
        return outp;
    }
}
