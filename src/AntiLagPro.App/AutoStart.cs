using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace AntiLagPro.App;

/// <summary>
/// Автозапуск при входе в Windows. Приложению нужны права админа
/// (requireAdministrator), а ключ реестра Run при логине НЕ умеет повышать
/// права — Windows молча его пропускает. Поэтому используем Планировщик задач
/// с триггером «при входе» и наивысшими правами: он запускает elevated-процесс
/// без запроса UAC.
/// </summary>
internal static class AutoStart
{
    private const string TaskName = "AntiLag";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AntiLag";

    public static bool IsEnabled()
    {
        var (code, _, _) = Run($"/Query /TN \"{TaskName}\"");
        return code == 0;
    }

    public static void Set(bool on)
    {
        // На всякий случай чистим старую (нерабочую) запись реестра.
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            k?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* не критично */ }

        if (on) CreateTask();
        else Run($"/Delete /TN \"{TaskName}\" /F");
    }

    private static void CreateTask()
    {
        string exe = Environment.ProcessPath ?? "";
        string xml = BuildXml(exe);

        string tmp = Path.Combine(Path.GetTempPath(), "antilag_task.xml");
        // schtasks /XML требует Unicode (UTF-16) файл.
        File.WriteAllText(tmp, xml, new UnicodeEncoding(false, true));
        try { Run($"/Create /TN \"{TaskName}\" /XML \"{tmp}\" /F"); }
        finally { try { File.Delete(tmp); } catch { } }
    }

    private static string BuildXml(string exe)
    {
        string cmd = SecurityElement.Escape(exe);
        return
$@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Автозапуск AntiLag — держит таймер 0.5 ms в фоне.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>false</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{cmd}</Command>
      <Arguments>--autostart</Arguments>
    </Exec>
  </Actions>
</Task>";
    }

    private static (int code, string outp, string err) Run(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd();
            string e = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, o, e);
        }
        catch (Exception ex) { return (-1, "", ex.Message); }
    }
}
