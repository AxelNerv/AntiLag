using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AntiLagPro.Core;

/// <summary>
/// Очистка оперативной памяти: освобождает рабочие наборы (working set) процессов.
/// Это безопасно и обратимо само собой — Windows подгрузит страницы обратно при нужде.
/// </summary>
public static class MemoryCleaner
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public static (double totalGB, double usedGB, double freeGB, int loadPercent) GetStatus()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref m);
        double total = m.ullTotalPhys / 1073741824.0;
        double free = m.ullAvailPhys / 1073741824.0;
        return (total, total - free, free, (int)m.dwMemoryLoad);
    }

    /// <summary>Чистит рабочие наборы всех доступных процессов. Возвращает ~МБ освобождённого.</summary>
    public static double Clean()
    {
        double before = GetStatus().freeGB;
        foreach (var p in Process.GetProcesses())
        {
            try { EmptyWorkingSet(p.Handle); }
            catch { /* защищённый/системный процесс — пропускаем */ }
            finally { p.Dispose(); }
        }
        Thread.Sleep(400);
        double after = GetStatus().freeGB;
        return Math.Max(0, (after - before) * 1024.0);
    }
}
