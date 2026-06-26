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
        NtQueryTimerResolution(out uint max, out uint min, out uint cur);
        // В API "maximum" = самое грубое значение (большое число), "minimum" = самое точное (маленькое).
        return (cur / 10000.0, min / 10000.0, max / 10000.0);
    }
}
