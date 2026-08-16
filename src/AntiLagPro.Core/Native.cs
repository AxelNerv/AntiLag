using System.Runtime.InteropServices;

namespace AntiLagPro.Core;

/// <summary>
/// Разрешение системного таймера. Значения — в тиках по 100 нс: 0.5 ms = 5000.
/// </summary>
internal static class Native
{
    [DllImport("ntdll.dll", SetLastError = true)]
    internal static extern int NtSetTimerResolution(uint desiredResolution, bool setResolution, out uint currentResolution);

    [DllImport("ntdll.dll", SetLastError = true)]
    internal static extern int NtQueryTimerResolution(out uint maximum, out uint minimum, out uint current);

    internal static (double current, double min, double max) QueryResolutionMs()
    {
        // В API "maximum" — самое грубое значение, "minimum" — самое точное.
        _ = NtQueryTimerResolution(out uint max, out uint min, out uint cur);
        return (cur / 10000.0, min / 10000.0, max / 10000.0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessPowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass,
                                                     ref ProcessPowerThrottlingState info, int size);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private const int ProcessPowerThrottling = 4;
    private const uint ThrottlingVersion1 = 1;
    private const uint ExecutionSpeed = 0x1;
    private const uint IgnoreTimerResolution = 0x4;

    /// <summary>StateMask = 0 означает «не применять к нам это ограничение».</summary>
    private static bool SetThrottling(uint controlMask)
    {
        var state = new ProcessPowerThrottlingState
        {
            Version = ThrottlingVersion1,
            ControlMask = controlMask,
            StateMask = 0,
        };
        return SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling,
                                     ref state, Marshal.SizeOf<ProcessPowerThrottlingState>());
    }

    /// <summary>
    /// Снимает игнорирование запросов таймера и эко-режим. Без этого Windows 10 2004+
    /// отвечает на NtSetTimerResolution успехом, а система остаётся на 1.0 ms.
    ///
    /// Обе маски — ОДНИМ вызовом: маска перечисляет всё, чем мы управляем сами, и
    /// второй вызов вернул бы системе управление тем, чего в новой маске нет.
    /// </summary>
    internal static bool RelaxThrottling()
    {
        if (SetThrottling(IgnoreTimerResolution | ExecutionSpeed)) return true;

        // Windows 10 до 2004 про таймерную маску не знает — снимем хотя бы эко-режим.
        _ = SetThrottling(ExecutionSpeed);
        return false;
    }
}
