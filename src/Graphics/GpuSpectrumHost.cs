using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BlueSpectrum.Graphics;

public sealed class GpuSpectrumHost : HwndHost
{
    private const string NativeClassName = "BlueSpectrum.GpuSurface";
    private static readonly WindowProcedure NativeProcedure = DefWindowProc;
    private static bool nativeClassRegistered;
    private nint childHandle;
    private nint parentHandle;
    private string? requestedAdapterId;
    public GpuRenderer? Renderer { get; private set; }
    public nint HostHandle => childHandle;
    public string Status => Renderer?.Status ?? "GPU를 준비하는 중";
    public Action<int>? LeftClick { get; set; }
    public Action? RightClick { get; set; }

    public string? RequestedAdapterId
    {
        get => requestedAdapterId;
        set
        {
            if (string.Equals(requestedAdapterId, value, StringComparison.OrdinalIgnoreCase)) return;
            requestedAdapterId = value;
            ReleaseRenderer();
        }
    }

    public GpuSpectrumHost()
    {
        Focusable = false;
        Unloaded += (_, _) => ReleaseRenderer();
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        parentHandle = hwndParent.Handle;
        RegisterNativeClass();
        // A native child with double-click messages, no border, and no separate taskbar entry.
        childHandle = CreateWindowEx(0, NativeClassName, "", 0x40000000 | 0x10000000 | 0x04000000,
            0, 0, Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight), parentHandle, 0, GetModuleHandle(null), 0);
        if (childHandle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new HandleRef(this, childHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        ReleaseRenderer();
        DestroyWindow(hwnd.Handle);
        childHandle = parentHandle = 0;
    }

    public void Render(GpuFrame frame)
    {
        VerifyAccess();
        if (childHandle == 0) return;
        Renderer ??= new GpuRenderer(childHandle, requestedAdapterId, frame.Width, frame.Height);
        Renderer.Render(frame);
    }

    public void ReleaseRenderer()
    {
        Renderer?.Dispose();
        Renderer = null;
    }

    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case 0x000F: // WM_PAINT: the next spectrum tick owns the DirectX pixels.
                ValidateRect(hwnd, 0);
                handled = true;
                return 0;
            case 0x0084: // WM_NCHITTEST: allow the outer WPF resize border through the child.
                var parentHit = SendMessage(parentHandle, msg, wParam, lParam);
                if (parentHit >= 10 && parentHit <= 17)
                {
                    handled = true;
                    return -1; // HTTRANSPARENT
                }
                break;
            case 0x0201: // WM_LBUTTONDOWN
                SetFocus(parentHandle);
                handled = true;
                LeftClick?.Invoke(1);
                return 0;
            case 0x0203: // WM_LBUTTONDBLCLK
                SetFocus(parentHandle);
                handled = true;
                LeftClick?.Invoke(2);
                return 0;
            case 0x0205: // WM_RBUTTONUP
                SetFocus(parentHandle);
                handled = true;
                RightClick?.Invoke();
                return 0;
            case 0x0007: // WM_SETFOCUS: preserve the WPF main window's keyboard shortcuts.
                SetFocus(parentHandle);
                handled = true;
                return 0;
            case 0x0014: // WM_ERASEBKGND: swap chain owns the pixels.
                handled = true;
                return 1;
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private static void RegisterNativeClass()
    {
        if (nativeClassRegistered) return;
        var windowClass = new NativeWindowClass
        {
            Style = 0x0008, // CS_DBLCLKS
            Procedure = Marshal.GetFunctionPointerForDelegate(NativeProcedure),
            Instance = GetModuleHandle(null), Cursor = LoadCursor(0, 32512),
            Background = GetStockObject(4), ClassName = NativeClassName
        };
        if (RegisterClass(ref windowClass) == 0 && Marshal.GetLastWin32Error() != 1410)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        nativeClassRegistered = true;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint WindowProcedure(nint hwnd, uint msg, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NativeWindowClass
    {
        public uint Style;
        public nint Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClass(ref NativeWindowClass data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint hwnd, int msg, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadCursor(nint instance, nint name);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ValidateRect(nint hwnd, nint rectangle);
}
