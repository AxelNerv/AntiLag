using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace AntiLagPro.Core;

/// <summary>
/// Живые метрики системы для дашборда: CPU%, память%, сеть (всего + скорость),
/// число процессов/приложений, аптайм. Без сторонних зависимостей.
/// </summary>
public sealed class SystemMonitor
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    private long _lIdle, _lKernel, _lUser;
    private long _lRecv, _lSent;
    private DateTime _lNet = DateTime.MinValue;

    public double CpuPercent()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user)) return 0;
        double cpu = 0;
        if (_lKernel != 0)
        {
            long sys = (kernel - _lKernel) + (user - _lUser); // kernel включает idle
            long idl = idle - _lIdle;
            if (sys > 0) cpu = (1.0 - (double)idl / sys) * 100.0;
        }
        _lIdle = idle; _lKernel = kernel; _lUser = user;
        return Math.Clamp(cpu, 0, 100);
    }

    public int MemPercent() => MemoryCleaner.GetStatus().loadPercent;

    public int Processes() => Process.GetProcesses().Length;

    public int Apps()
    {
        int n = 0;
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.MainWindowHandle != IntPtr.Zero) n++; }
            catch { }
            finally { p.Dispose(); }
        }
        return n;
    }

    public string Uptime()
    {
        var t = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return $"{(int)t.TotalDays:00}:{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    /// <summary>Всего передано (ГБ) и скорость вверх/вниз (байт/сек) с прошлого вызова.</summary>
    public (double totalGB, double up, double down) Network()
    {
        long recv = 0, sent = 0;
        try
        {
            var nic = ActiveNic();
            if (nic is not null) { var s = nic.GetIPStatistics(); recv = s.BytesReceived; sent = s.BytesSent; }
        }
        catch { }

        double up = 0, down = 0;
        var now = DateTime.UtcNow;
        if (_lNet != DateTime.MinValue)
        {
            double secs = (now - _lNet).TotalSeconds;
            if (secs > 0.1)
            {
                down = Math.Max(0, (recv - _lRecv) / secs);
                up = Math.Max(0, (sent - _lSent) / secs);
            }
        }
        _lRecv = recv; _lSent = sent; _lNet = now;
        return ((recv + sent) / 1_000_000_000.0, up, down);
    }

    public static string Speed(double bytesPerSec)
    {
        if (bytesPerSec >= 1_000_000) return $"{bytesPerSec / 1_000_000:N1} МБ/с";
        if (bytesPerSec >= 1_000) return $"{bytesPerSec / 1_000:N0} КБ/с";
        return $"{bytesPerSec:N0} Б/с";
    }

    private static NetworkInterface? ActiveNic()
    {
        IPAddress? local = null;
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 65530);
            local = (s.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch { }

        var all = NetworkInterface.GetAllNetworkInterfaces();
        if (local is not null)
        {
            var m = all.FirstOrDefault(n => n.GetIPProperties().UnicastAddresses.Any(u => u.Address.Equals(local)));
            if (m is not null) return m;
        }
        return all.FirstOrDefault(n =>
            n.OperationalStatus == OperationalStatus.Up &&
            n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
    }
}
