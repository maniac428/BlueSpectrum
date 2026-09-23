using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BlueSpectrum.UI;

internal static class BlackWindowFrame
{
    // Windows 11 DWM attributes apply only to this app's HWND, not the desktop theme.
    private const int CornerPreference = 33, BorderColor = 34, CaptionColor = 35;
    private const uint ColorNone = 0xFFFFFFFE;
    private static readonly DependencyProperty AcceptedProperty = DependencyProperty.RegisterAttached("BorderSuppressionAccepted", typeof(bool?), typeof(BlackWindowFrame), new PropertyMetadata(null));
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

    internal static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => Apply(window);
        window.Activated += (_, _) => Apply(window);
        window.Deactivated += (_, _) => Apply(window);
    }
    private static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        uint none = ColorNone, black = 0, square = 1;
        // Older systems may reject these optional attributes; the WPF content stays black.
        int result = DwmSetWindowAttribute(hwnd, BorderColor, ref none, sizeof(uint));
        window.SetValue(AcceptedProperty, result == 0);
        DwmSetWindowAttribute(hwnd, CaptionColor, ref black, sizeof(uint));
        DwmSetWindowAttribute(hwnd, CornerPreference, ref square, sizeof(uint));
    }
    // BORDER_COLOR is a documented setter; report the accepted request, not a guessed getter result.
    internal static bool? BorderSuppressionAccepted(Window window) => (bool?)window.GetValue(AcceptedProperty);
}
