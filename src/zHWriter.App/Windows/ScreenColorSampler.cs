using System.Runtime.InteropServices;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace zHWriter.App.Windows;

/// <summary>Samples the composited desktop under a transparent WPF window at a deliberately low frequency.</summary>
public static class ScreenColorSampler
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr deviceContext, int x, int y);

    public static bool TryGetScreenColor(int x, int y, out WpfColor color)
    {
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) { color = default; return false; }
        try
        {
            var rgb = GetPixel(dc, x, y);
            if (rgb == 0xFFFFFFFF) { color = default; return false; }
            color = WpfColor.FromRgb((byte)(rgb & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)((rgb >> 16) & 0xFF));
            return true;
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }

    public static bool IsLight(WpfColor color) => ((color.R * 0.2126) + (color.G * 0.7152) + (color.B * 0.0722)) >= 148;
}
