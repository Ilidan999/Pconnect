using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Pconnect;

// ---------- «жидкое стекло»: полупрозрачное окно + размытие фона ----------
// Размытие делается через SetWindowCompositionAttribute (accent blur).
// Окно должно быть AllowsTransparency="True" — тогда прозрачный фон никогда не станет чёрным.
static class Glass
{
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

    const int WCA_ACCENT_POLICY = 19;
    const int ACCENT_DISABLED = 0;
    const int ACCENT_ENABLE_BLURBEHIND = 3;
    const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    // mode: "acrylic" | "mica" | "none"
    // surface — корневой Border, которому задаём полупрозрачную/непрозрачную подложку
    public static void Apply(Window w, Border surface, string mode)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;

            if (mode == "none")
            {
                SetAccent(hwnd, ACCENT_DISABLED, 0, 0);
                SetSurface(surface, OpaqueGradient(), null);
            }
            else if (mode == "acrylic")
            {
                // сильное «стекло»: акрил (размытие + оттенок), если не выйдет — обычное размытие
                if (!SetAccent(hwnd, ACCENT_ENABLE_ACRYLICBLURBEHIND, 2, 0x40FFFFFF))
                    SetAccent(hwnd, ACCENT_ENABLE_BLURBEHIND, 0, 0);
                SetSurface(surface, GlassBrush(0x80), GlassBrush(0x40));
            }
            else // mica
            {
                // мягкое «стекло»: размытие + более плотная подложка
                SetAccent(hwnd, ACCENT_ENABLE_BLURBEHIND, 0, 0);
                SetSurface(surface, GlassBrush(0xA6), GlassBrush(0x33));
            }
        }
        catch { }
    }

    static void SetSurface(Border surface, Brush background, Brush border)
    {
        if (surface == null) return;
        surface.Background = background;
        surface.BorderBrush = border;
    }

    static Brush GlassBrush(byte alpha) =>
        new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF));

    static Brush OpaqueGradient()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F6F7FC"), 0));
        g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#E8EBF5"), 1));
        return g;
    }

    static bool SetAccent(IntPtr hwnd, int state, int flags, int gradientColor)
    {
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = state,
                AccentFlags = flags,
                GradientColor = gradientColor,
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
}
