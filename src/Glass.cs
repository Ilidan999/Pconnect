using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Pconnect;

// ---------- настоящий «жидкое стекло»: Mica / Acrylic через DWM ----------
static class Glass
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [StructLayout(LayoutKind.Sequential)]
    struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor; // 0xAABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    const int DWMWA_MICA_EFFECT = 1029;

    const int DWMSBT_NONE = 1;
    const int DWMSBT_MAINWINDOW = 2;      // Mica
    const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

    const int WCA_ACCENT_POLICY = 19;
    const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    // mode: "mica" (по умолчанию) | "acrylic" | "none"
    public static void Apply(Window w, string mode)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int build = Environment.OSVersion.Version.Build;

            // Принудительно светлая тема окна, чтобы Mica была светлой (приложение светлое)
            if (build >= 22000)
            {
                int dark = 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }

            bool glass = false;
            if (build >= 22621)
            {
                int backdrop = mode == "acrylic" ? DWMSBT_TRANSIENTWINDOW
                             : mode == "none" ? DWMSBT_NONE
                             : DWMSBT_MAINWINDOW;
                int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
                glass = mode != "none" && hr == 0;
            }
            else if (build >= 22000)
            {
                int mica = (mode == "none") ? 0 : 1;
                int hr = DwmSetWindowAttribute(hwnd, DWMWA_MICA_EFFECT, ref mica, sizeof(int));
                glass = mode != "none" && hr == 0;
            }
            else if (mode != "none")
            {
                glass = ApplyWin10Acrylic(hwnd);
            }

            if (!glass)
                w.Background = FallbackBrush();
        }
        catch
        {
            try { w.Background = FallbackBrush(); } catch { }
        }
    }

    static bool ApplyWin10Acrylic(IntPtr hwnd)
    {
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = 0x40FFFFFF, // лёгкий белый оттенок
                AnimationId = 0
            };
            int size = Marshal.SizeOf<AccentPolicy>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    SizeOfData = size,
                    Data = ptr
                };
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch { return false; }
    }

    static Brush FallbackBrush()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F6F7FC"), 0));
        g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#E8EBF5"), 1));
        return g;
    }
}
