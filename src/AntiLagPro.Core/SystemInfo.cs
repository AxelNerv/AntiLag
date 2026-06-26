namespace AntiLagPro.Core;

/// <summary>Статическая сводка о железе для бенто-дашборда «О системе».</summary>
public sealed record SysSummary(
    string Board, string Cpu, string Gpu,
    double RamGB, double StorageTB, string Windows, string Wei);

public static class SystemInfo
{
    public static SysSummary GetSummary()
    {
        string board = "—", cpu = "—", gpu = "—", win = "—", wei = "—";
        try
        {
            string c =
                "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; " +
                "$b=Get-CimInstance Win32_BaseBoard; " +
                "$p=Get-CimInstance Win32_Processor|Select-Object -First 1; " +
                "$o=Get-CimInstance Win32_OperatingSystem; " +
                "$g=(Get-CimInstance Win32_VideoController|Where-Object {$_.AdapterRAM -gt 0}|ForEach-Object{$_.Name}) -join ', '; " +
                "$w=try{(Get-CimInstance Win32_WinSAT).WinSPRLevel}catch{''}; " +
                "('B~'+$b.Product); ('C~'+$p.Name.Trim()); ('G~'+$g); " +
                "('O~'+$o.Caption+' (build '+$o.BuildNumber+')'); ('W~'+$w)";

            var (_, outp, _) = ProcessRunner.Run("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -Command \"{c}\"");

            foreach (var raw in outp.Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length < 2 || l[1] != '~') continue;
                string v = l.Substring(2).Trim();
                switch (l[0])
                {
                    case 'B': board = v; break;
                    case 'C': cpu = v; break;
                    case 'G': gpu = v; break;
                    case 'O': win = v; break;
                    case 'W': wei = v; break;
                }
            }
        }
        catch { /* частичная инфа лучше, чем ничего */ }

        double ram = MemoryCleaner.GetStatus().totalGB;

        double storageBytes = 0;
        try
        {
            foreach (var d in DriveInfo.GetDrives())
                if (d.IsReady && d.DriveType == DriveType.Fixed) storageBytes += d.TotalSize;
        }
        catch { }

        return new SysSummary(
            string.IsNullOrWhiteSpace(board) ? "—" : board,
            cpu, string.IsNullOrWhiteSpace(gpu) ? "—" : gpu,
            ram, storageBytes / 1_000_000_000_000.0, win,
            string.IsNullOrWhiteSpace(wei) ? "—" : wei);
    }
}
