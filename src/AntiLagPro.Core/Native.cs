using System.Runtime.InteropServices;

namespace AntiLagPro.Core;

/// <summary>
/// P/Invoke в ntdll для управления Timer Resolution (разрешение системного таймера).
/// Значения задаются в "тиках" по 100 наносекунд: 0.5 ms = 5000, 1.0 ms = 10000.
/// </summary>
internal static class Native
{
    [DllImport("ntdll.dll", SetLastError = true)]
    internal static extern int NtSetTimerResolution(uint desiredResolution, bool setResolution, out uint currentResolution);

    [DllImport("ntdll.dll", SetLastError = true)]
    internal static extern int NtQueryTimerResolution(out uint maximum, out uint minimum, out uint current);

    /// <summary>Текущее/мин/макс разрешение таймера в миллисекундах.</summary>
    internal static (double current, double min, double max) QueryResolutionMs()
    {
        _ = NtQueryTimerResolution(out uint max, out uint min, out uint cur);
        // В API "maximum" = самое грубое значение (большое число), "minimum" = самое точное (маленькое).
        return (cur / 10000.0, min / 10000.0, max / 10000.0);
    }

    // --- Энергосбережение процесса (EcoQoS) ---

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

    /// <summary>
    /// Общая часть: включить/выключить конкретный вид троттлинга для своего процесса.
    /// StateMask = 0 означает «не применять к нам этот вид ограничения».
    /// </summary>
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
    /// Снять с процесса два ограничения Windows: игнорирование запросов разрешения
    /// таймера и придушивание скорости (EcoQoS).
    ///
    /// Начиная с Windows 10 2004 процессы, объявившие в манифесте совместимость с
    /// Windows 10/11, попадают в «щадящий» режим: NtSetTimerResolution отвечает
    /// успехом, но система остаётся на 1.0 ms. Снимается это только здесь.
    ///
    /// ВАЖНО: обе маски задаются ОДНИМ вызовом. Маска перечисляет всё, чем мы
    /// управляем сами, поэтому второй вызов с другой маской возвращает системе
    /// управление тем, чего в новой маске нет — и таймер снова начинает игнорироваться.
    /// </summary>
    internal static bool RelaxThrottling()
    {
        if (SetThrottling(IgnoreTimerResolution | ExecutionSpeed)) return true;

        // Windows 10 до 2004 про таймерную маску не знает — снимем хотя бы эко-режим.
        _ = SetThrottling(ExecutionSpeed);
        return false;
    }
}
