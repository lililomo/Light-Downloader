using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LightDownloader.App;

/// <summary>Gives a window the native dark title bar in the app's surface colour (Windows 10 20H1+ / 11).</summary>
internal static class WindowTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int on = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
            int caption = 0x00241D1A; // COLORREF (0x00BBGGRR) of #1A1D24
            DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));
            int border = 0x003A302B;  // #2B303A
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(int));
        };
    }
}
