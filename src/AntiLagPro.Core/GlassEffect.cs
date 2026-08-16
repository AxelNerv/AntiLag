using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AntiLagPro.Core;

/// <summary>
/// Слюда/Акрил/Вкладки — системная подложка DWM (цвет задаёт Windows).
/// Размытие и Заливка — композиция окна: тут можно задать свой цвет и прозрачность.
/// </summary>
public enum GlassKind { Mica = 2, Acrylic = 3, Tabbed = 4, Blur = 10, Tint = 11 }

/// <summary>
/// Прозрачный фон у окон проводника. Известный ExplorerBlurMica делает это
/// инъекцией DLL в explorer.exe — мы так не делаем принципиально и работаем
/// снаружи, официальным DWM API. Эффект слабее, зато безопасно.
/// </summary>
public static class GlassEffect
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc cb, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    // Недокументированный, но давно используемый API композиции (так же работает
    // TranslucentTB): применяется к чужим окнам без инъекции.
    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WinCompAttrData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;   // 0xAABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinCompAttrData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_GRADIENT = 2;              // сплошная заливка цветом
    private const int ACCENT_ACRYLICBLURBEHIND = 4;     // размытие + оттенок

    private delegate bool EnumProc(IntPtr hwnd, IntPtr param);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int BACKDROP_NONE = 1;

    // перерисовать рамку, не двигая и не меняя размер окна
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_FRAMECHANGED = 0x20;

    /// <summary>Системная подложка появилась в Windows 11 22H2 (сборка 22621).</summary>
    public static bool Supported => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>Окна проводника: папки и диалоги «Открыть/Сохранить».</summary>
    private static readonly string[] Classes = { "CabinetWClass", "ExploreWClass" };

    private static System.Threading.Timer? _watcher;

    public static bool IsRunning => _watcher is not null;

    /// <summary>Цвет и прозрачность действуют в режимах «Размытие» и «Заливка».</summary>
    public static void Enable(GlassKind kind, byte r = 32, byte g = 32, byte b = 38, byte alpha = 160, bool darkTitle = true)
    {
        _watcher?.Dispose();
        Apply(kind, r, g, b, alpha, darkTitle);
        // новые окна появляются постоянно — подхватываем их
        _watcher = new System.Threading.Timer(_ => Apply(kind, r, g, b, alpha, darkTitle), null, 1500, 1500);
    }

    public static void Disable()
    {
        _watcher?.Dispose();
        _watcher = null;
        Apply(kind: null, 0, 0, 0, 0, darkTitle: false);
    }

    /// <summary>
    /// Применяет эффект ко всем окнам проводника (kind = null — снять).
    /// DwmExtendFrameIntoClientArea тут применять НЕЛЬЗЯ: на чужом окне DWM ждёт
    /// прозрачный фон, которого проводник не рисует, и окно становится белым.
    /// </summary>
    private static void Apply(GlassKind? kind, byte r, byte g, byte b, byte alpha, bool darkTitle)
    {
        var windows = new List<IntPtr>();
        try
        {
            _ = EnumWindows((hwnd, _) =>
            {
                if (IsWindowVisible(hwnd) && IsExplorerWindow(hwnd)) windows.Add(hwnd);
                return true;
            }, IntPtr.Zero);
        }
        catch { }

        bool composition = kind is GlassKind.Blur or GlassKind.Tint;

        foreach (var hwnd in windows)
        {
            try
            {
                // 1) системная подложка DWM
                int backdrop = kind is null || composition ? BACKDROP_NONE : (int)kind;
                _ = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

                if (kind is not null)
                {
                    int dark = darkTitle ? 1 : 0;
                    _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                }

                // 2) композиция окна — свой цвет и прозрачность
                SetAccent(hwnd,
                    state: composition ? (kind == GlassKind.Blur ? ACCENT_ACRYLICBLURBEHIND : ACCENT_GRADIENT) : ACCENT_DISABLED,
                    color: (uint)(alpha << 24 | b << 16 | g << 8 | r));

                _ = SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                             SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
            }
            catch (Exception ex) { Log.Warn("Не применить эффект к окну проводника", ex); }
        }
    }

    private static void SetAccent(IntPtr hwnd, int state, uint color)
    {
        var policy = new AccentPolicy { AccentState = state, AccentFlags = 2, GradientColor = color };
        int size = Marshal.SizeOf(policy);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WinCompAttrData { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
            _ = SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static bool IsExplorerWindow(IntPtr hwnd)
    {
        var sb = new StringBuilder(64);
        if (GetClassName(hwnd, sb, sb.Capacity) == 0) return false;
        string cls = sb.ToString();
        return Classes.Any(c => string.Equals(c, cls, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Системная прозрачность («Эффекты прозрачности» в параметрах).</summary>
    public static bool SystemTransparency
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("EnableTransparency") is not int v || v == 1;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            k.SetValue("EnableTransparency", value ? 1 : 0, RegistryValueKind.DWord);
        }
    }
}
