using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BlueSpectrum.UI;

internal enum WindowPosition { Center, TopLeft, BottomLeft, TopRight, BottomRight }

internal static class WindowPositioning
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint NoSize = 0x0001, NoZOrder = 0x0004, NoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    /// <summary>
    /// Returns the current monitor's usable area in physical virtual-screen pixels.
    /// Requires this app's PerMonitorV2 DPI context; an unavailable native window returns Rect.Empty.
    /// </summary>
    internal static Rect GetWorkArea(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return Rect.Empty;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return Rect.Empty;
        var r = info.Work;
        if (r.Right <= r.Left || r.Bottom <= r.Top) return Rect.Empty;
        return new Rect(r.Left, r.Top, (double)r.Right - r.Left, (double)r.Bottom - r.Top);
    }

    /// <summary>Returns native outer bounds in physical virtual-screen pixels, or Rect.Empty if unavailable.</summary>
    internal static Rect GetBounds(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return Rect.Empty;
        if (r.Right <= r.Left || r.Bottom <= r.Top) return Rect.Empty;
        return new Rect(r.Left, r.Top, (double)r.Right - r.Left, (double)r.Bottom - r.Top);
    }

    /// <summary>
    /// Moves a normal window within a previously captured physical-pixel work area,
    /// preserving its native size, activation and Z order. Call after leaving fullscreen/maximized state.
    /// </summary>
    internal static bool Move(Window window, WindowPosition position, Rect workArea)
    {
        if (window.WindowState != WindowState.Normal || !IsValid(workArea) || !Enum.IsDefined(position)) return false;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return false;
        if (r.Right <= r.Left || r.Bottom <= r.Top) return false;
        var target = CalcTarget(workArea, new Size((double)r.Right - r.Left, (double)r.Bottom - r.Top), position);
        if (target.X < int.MinValue || target.X > int.MaxValue || target.Y < int.MinValue || target.Y > int.MaxValue) return false;
        return SetWindowPos(hwnd, IntPtr.Zero, (int)target.X, (int)target.Y, 0, 0, NoSize | NoZOrder | NoActivate);
    }

    /// <summary>
    /// Calculates a top-left position in physical pixels, rounded to the nearest pixel.
    /// Oversized windows align to the work area's top/left on each overflowing axis without resizing.
    /// </summary>
    internal static Point CalcTarget(Rect work, Size windowSize, WindowPosition position)
    {
        if (!IsValid(work)) throw new ArgumentOutOfRangeException(nameof(work));
        if (windowSize.IsEmpty || !double.IsFinite(windowSize.Width) || !double.IsFinite(windowSize.Height)
            || windowSize.Width <= 0 || windowSize.Height <= 0) throw new ArgumentOutOfRangeException(nameof(windowSize));
        if (!Enum.IsDefined(position)) throw new ArgumentOutOfRangeException(nameof(position));

        double remainingX = Math.Max(0, work.Width - windowSize.Width);
        double remainingY = Math.Max(0, work.Height - windowSize.Height);
        double x = work.Left + (position switch
        {
            WindowPosition.Center => remainingX / 2,
            WindowPosition.TopRight or WindowPosition.BottomRight => remainingX,
            _ => 0
        });
        double y = work.Top + (position switch
        {
            WindowPosition.Center => remainingY / 2,
            WindowPosition.BottomLeft or WindowPosition.BottomRight => remainingY,
            _ => 0
        });
        return new Point(Math.Round(x, MidpointRounding.AwayFromZero), Math.Round(y, MidpointRounding.AwayFromZero));
    }

    private static bool IsValid(Rect rect) => !rect.IsEmpty && rect.Width > 0 && rect.Height > 0
        && double.IsFinite(rect.Left) && double.IsFinite(rect.Top)
        && double.IsFinite(rect.Right) && double.IsFinite(rect.Bottom);
}
