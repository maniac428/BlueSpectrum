using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using PixelFormat = Vortice.DCommon.PixelFormat;
using D2DAlphaMode = Vortice.DCommon.AlphaMode;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;

namespace BlueSpectrum.Graphics;

public readonly record struct GpuRectangle(float X, float Y, float Width, float Height, uint Argb);
public sealed record GpuFrame(int Width, int Height, long BackgroundVersion, byte[] BackgroundPixels, IReadOnlyList<GpuRectangle> Rectangles);

/// <summary>Draws spectrum rectangles directly on the explicitly selected D3D11 adapter.</summary>
public sealed class GpuRenderer : IDisposable
{
    private IDXGIFactory2? factory;
    private ID3D11Device? device;
    private ID3D11DeviceContext? deviceContext;
    private IDXGISwapChain1? swapChain;
    private ID2D1Factory? d2dFactory;
    private ID3D11Texture2D? backBuffer;
    private ID2D1RenderTarget? target;
    private ID2D1SolidColorBrush? brush;
    private ID2D1Bitmap? background;
    private long backgroundVersion = long.MinValue;
    private GpuFrame? lastFrame;
    private bool disposed;

    public GpuAdapterInfo ActiveAdapter { get; private set; } = new("", "");
    public long ActiveAdapterLuid { get; private set; }
    public string Status { get; private set; } = "GPU를 준비하는 중";
    public bool RequestedAdapterMissing { get; private set; }
    public long RenderedFrameCount { get; private set; }
    public int PixelWidth { get; private set; }
    public int PixelHeight { get; private set; }

    public GpuRenderer(nint hwnd, string? requestedId, int pixelWidth, int pixelHeight)
    {
        if (hwnd == 0) throw new ArgumentException("유효한 그래프 창이 필요합니다.", nameof(hwnd));
        try
        {
            factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
            var adapters = GpuCatalog.OpenAdapters(factory);
            try
            {
                var selected = string.IsNullOrWhiteSpace(requestedId) ? null : adapters.FirstOrDefault(x => string.Equals(x.Info.Id, requestedId, StringComparison.OrdinalIgnoreCase));
                RequestedAdapterMissing = !string.IsNullOrWhiteSpace(requestedId) && selected == null;
                selected ??= GpuCatalog.SelectAutomatic(adapters, hwnd);
                FeatureLevel[] levels = [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];
                D3D11.D3D11CreateDevice(selected.Adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, levels,
                    out device, out _, out deviceContext).CheckError();
                using var actualDevice = device.QueryInterface<IDXGIDevice>();
                using var actualAdapter = actualDevice.GetAdapter();
                var actual = actualAdapter.Description;
                if (actual.Luid != selected.Adapter.Description1.Luid)
                    throw new InvalidOperationException("선택한 GPU와 실제 그래프 장치가 일치하지 않습니다.");
                ActiveAdapter = new GpuAdapterInfo(selected.Info.Id, actual.Description.Trim());
                ActiveAdapterLuid = actual.Luid;
                Status = RequestedAdapterMissing
                    ? $"선택한 GPU를 찾을 수 없어 자동 선택: {ActiveAdapter.Name}"
                    : $"그래프 GPU: {ActiveAdapter.Name}";
            }
            finally { foreach (var item in adapters) item.Adapter.Dispose(); }

            PixelWidth = Math.Max(1, pixelWidth);
            PixelHeight = Math.Max(1, pixelHeight);
            var description = new SwapChainDescription1
            {
                Width = (uint)PixelWidth, Height = (uint)PixelHeight,
                Format = Format.B8G8R8A8_UNorm, BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput, SampleDescription = SampleDescription.Default,
                Scaling = Scaling.Stretch, SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = Vortice.DXGI.AlphaMode.Ignore
            };
            swapChain = factory.CreateSwapChainForHwnd(device, hwnd, description);
            factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter).CheckError();
            d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.SingleThreaded);
            CreateTarget();
        }
        catch { Dispose(); throw; }
    }

    private void CreateTarget()
    {
        backBuffer = swapChain!.GetBuffer<ID3D11Texture2D>(0);
        using var surface = backBuffer.QueryInterface<IDXGISurface>();
        target = d2dFactory!.CreateDxgiSurfaceRenderTarget(surface, new RenderTargetProperties(new PixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Ignore)));
        // Frames use physical pixels, so D2D's coordinate system remains 96 DPI.
        target.SetDpi(96, 96);
        target.AntialiasMode = AntialiasMode.PerPrimitive;
        brush = target.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
    }

    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        width = Math.Max(1, width); height = Math.Max(1, height);
        if (width == PixelWidth && height == PixelHeight) return;
        ReleaseTarget();
        deviceContext!.ClearState();
        deviceContext.Flush();
        swapChain!.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
        PixelWidth = width; PixelHeight = height;
        lastFrame = null;
        CreateTarget();
    }

    public void Render(GpuFrame frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (frame.Width <= 0 || frame.Height <= 0 || frame.BackgroundPixels.Length != checked(frame.Width * frame.Height * 4))
            throw new ArgumentException("그래프 배경 픽셀 크기가 올바르지 않습니다.", nameof(frame));
        Resize(frame.Width, frame.Height);
        UpdateBackground(frame);
        Draw(frame);
        swapChain!.Present(0, PresentFlags.None).CheckError();
        lastFrame = frame;
        RenderedFrameCount++;
    }

    private unsafe void UpdateBackground(GpuFrame frame)
    {
        if (background != null && backgroundVersion == frame.BackgroundVersion) return;
        background?.Dispose();
        background = null;
        fixed (byte* pixels = frame.BackgroundPixels)
            background = target!.CreateBitmap(new SizeI(frame.Width, frame.Height), (nint)pixels, (uint)(frame.Width * 4),
                new BitmapProperties(new PixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied), 96, 96));
        backgroundVersion = frame.BackgroundVersion;
    }

    private void Draw(GpuFrame frame)
    {
        target!.BeginDraw();
        target.Clear(new Color4(0, 0, 0, 1));
        target.DrawBitmap(background!);
        foreach (var rectangle in frame.Rectangles)
        {
            if (rectangle.Width <= 0 || rectangle.Height <= 0 || (rectangle.Argb >> 24) == 0) continue;
            var argb = rectangle.Argb;
            brush!.Color = new Color4(((argb >> 16) & 255) / 255f, ((argb >> 8) & 255) / 255f, (argb & 255) / 255f, (argb >> 24) / 255f);
            target.FillRectangle(new RawRectF(rectangle.X, rectangle.Y, rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height), brush);
        }
        target.EndDraw().CheckError();
    }

    /// <summary>Diagnostics only: redraw the last frame and copy its GPU pixels to CPU memory.</summary>
    public byte[] ReadPixels()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (lastFrame == null) throw new InvalidOperationException("아직 그린 GPU 프레임이 없습니다.");
        // Flip-model presentation advances buffers. Redraw without presenting for deterministic capture.
        Draw(lastFrame);
        var description = backBuffer!.Description;
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        description.MiscFlags = ResourceOptionFlags.None;
        using var staging = device!.CreateTexture2D(description);
        deviceContext!.CopyResource(staging, backBuffer);
        var mapped = deviceContext.Map(staging, 0, MapMode.Read);
        try
        {
            var stride = checked(PixelWidth * 4);
            var pixels = new byte[checked(stride * PixelHeight)];
            for (var y = 0; y < PixelHeight; y++)
                Marshal.Copy(mapped.DataPointer + checked((int)mapped.RowPitch * y), pixels, y * stride, stride);
            return pixels;
        }
        finally { deviceContext.Unmap(staging, 0); }
    }

    private void ReleaseTarget()
    {
        background?.Dispose(); background = null;
        backgroundVersion = long.MinValue;
        brush?.Dispose(); brush = null;
        target?.Dispose(); target = null;
        backBuffer?.Dispose(); backBuffer = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ReleaseTarget();
        deviceContext?.ClearState();
        deviceContext?.Flush();
        swapChain?.Dispose(); swapChain = null;
        deviceContext?.Dispose(); deviceContext = null;
        device?.Dispose(); device = null;
        d2dFactory?.Dispose(); d2dFactory = null;
        factory?.Dispose(); factory = null;
        lastFrame = null;
    }
}
