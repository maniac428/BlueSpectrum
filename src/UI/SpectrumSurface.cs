using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlueSpectrum.Graphics;

namespace BlueSpectrum.UI;

public sealed class SpectrumSurface : Grid
{
    private const int Steps = 13;
    private const double PanelWidth = 354, PanelHeight = 146, PlotTop = 19, PlotHeight = 100;
    private static readonly string[] Frequencies = ["63Hz", "160Hz", "400Hz", "1kHz", "2.5kHz", "6.3kHz", "16kHz", "FULL RANGE"];
    private readonly double[][] levels = [new double[8], new double[8]];
    private readonly double[][] peaks = [new double[8], new double[8]];
    private readonly double[][] holds = [new double[8], new double[8]];
    private readonly Dictionary<string, FormattedText> textCache = new();
    private readonly Typeface lettering = new("Arial");
    private DrawingGroup? face;
    private readonly DrawingGroup[,] activeSegments = new DrawingGroup[8, Steps];
    private Brush lit = Brushes.LightBlue, halo = Brushes.Blue, outerHalo = Brushes.Blue;
    private static readonly Brush Inactive = Brush("#061119"), Printed = Brush("#909DA5"), Dormant = Brush("#16212A"), ScaleInk = Brush("#A0CFE8");
    private static readonly Brush RedCore = Brush("#D7645C"), RedHalo = Brush("#28100F");
    private double lastBrightness = -1, lastGlow = -1;
    private GpuSpectrumHost? gpuHost;
    private byte[]? backgroundPixels;
    private DrawingGroup? backgroundFace;
    private int backgroundWidth, backgroundHeight;
    private long backgroundVersion;
    private readonly List<GpuRectangle> gpuRectangles = new(1300);
    private DateTime retryGpuAfter;
    private string? gpuFailure;
    internal bool UseGpuRendering { get; set; }
    internal Action<int>? NativeLeftClick { get; set; }
    internal GpuSpectrumHost? GpuHost => gpuHost;
    internal string RendererStatus => gpuFailure ?? gpuHost?.Status ?? (UseGpuRendering ? "GPU 연결 준비 중" : "기본 화면 표시");
    public AppSettings Settings { get; set; } = new();
    public bool IsCapturing { get; set; }
    public string InputCaption { get; set; } = "출력 장치를 연결하는 중";

    public SpectrumSurface()
    {
        ClipToBounds = true;
        ToolTip = "오른쪽 0~36은 상대 표시 레벨입니다. 왼쪽 ±12는 원기기의 EQ 눈금을 재현한 장식이며 실제 조절값이 아닙니다.";
        Background = Brushes.Black;
        Loaded += (_, _) => { if (UseGpuRendering) Dispatcher.BeginInvoke(() => RenderGpu()); };
        Unloaded += (_, _) => ReleaseGpu();
        SizeChanged += (_, _) => { backgroundPixels = null; if (UseGpuRendering && IsLoaded) Dispatcher.BeginInvoke(() => RenderGpu()); };
    }

    // The original 0..36 markings are a visual relative scale, not calibrated dBFS.
    // Full range is measured independently over the captured signal, not summed from seven bars.
    public void Update(double[] left, double[] right, double elapsedSeconds, bool hasRecentAudio,
        double leftFullRangeDb = -100, double rightFullRangeDb = -100)
    {
        double dt = Math.Clamp(elapsedSeconds, .001, .25);
        for (int c = 0; c < 2; c++)
        for (int b = 0; b < 8; b++)
        {
            double db = (b == 7 ? c == 0 ? leftFullRangeDb : rightFullRangeDb : c == 0 ? left[b] : right[b]) + Settings.GainDb;
            double target = hasRecentAudio && double.IsFinite(db) ? Math.Clamp((db + 48) / 36, 0, 1) : 0;
            double tau = (target > levels[c][b] ? Settings.AttackMs : Settings.ReleaseMs) / 1000;
            levels[c][b] += (target - levels[c][b]) * (1 - Math.Exp(-dt / tau));
            if (levels[c][b] > peaks[c][b]) { peaks[c][b] = levels[c][b]; holds[c][b] = .8; }
            else if (holds[c][b] > 0) holds[c][b] -= dt;
            else peaks[c][b] = Math.Max(levels[c][b], peaks[c][b] - dt * .42);
        }
        if (!RenderGpu()) InvalidateVisual();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        face = null; backgroundPixels = null; textCache.Clear(); base.OnDpiChanged(oldDpi, newDpi);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (gpuHost?.Renderer != null && gpuFailure == null) return;
        if (ActualWidth < 100 || ActualHeight < 50) return;
        EnsureDrawings();
        const double gap = 8;
        double scale = Math.Min((ActualWidth - 6) / (PanelWidth * 2 + gap), (ActualHeight - 4) / PanelHeight);
        if (scale <= 0) return;
        double x = (ActualWidth - (PanelWidth * 2 + gap) * scale) / 2;
        double y = (ActualHeight - PanelHeight * scale) / 2;
        for (int channel = 0; channel < 2; channel++)
        {
            dc.PushTransform(new TranslateTransform(x + channel * (PanelWidth + gap) * scale, y));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawDrawing(face);
            for (int band = 0; band < 8; band++)
            {
                int count = Math.Clamp((int)Math.Ceiling(levels[channel][band] * Steps - .025), 0, Steps);
                int peak = Math.Clamp((int)Math.Ceiling(peaks[channel][band] * Steps - .025), 0, Steps);
                for (int row = 0; row < count; row++) dc.DrawDrawing(activeSegments[band, row]);
                if (Settings.PeakHold && peak > count)
                {
                    dc.PushOpacity(.65); dc.DrawDrawing(activeSegments[band, peak - 1]); dc.Pop();
                }
            }
            Text(dc, channel == 0 ? "LEFT" : "RIGHT", 8, 3, 8.2, Printed);
            dc.Pop(); dc.Pop();
        }
    }

    internal void ApplyGpuSelection()
    {
        retryGpuAfter = default; gpuFailure = null;
        ReleaseGpu();
        if (UseGpuRendering && IsLoaded) { EnsureGpuHost(); Dispatcher.BeginInvoke(() => RenderGpu()); }
        InvalidateVisual();
    }

    private void EnsureGpuHost()
    {
        if (gpuHost != null) return;
        gpuHost = new GpuSpectrumHost { RequestedAdapterId = Settings.RenderGpuId };
        gpuHost.LeftClick = count => NativeLeftClick?.Invoke(count);
        gpuHost.RightClick = () =>
        {
            var menu = ContextMenu;
            for (DependencyObject? p = this; menu == null && p != null; p = VisualTreeHelper.GetParent(p))
                if (p is FrameworkElement element) menu = element.ContextMenu;
            if (menu != null) { menu.PlacementTarget = this; menu.IsOpen = true; }
        };
        Children.Add(gpuHost);
        UpdateLayout();
    }

    internal void ReleaseGpu()
    {
        if (gpuHost == null) return;
        var oldHost = gpuHost; gpuHost = null;
        Children.Remove(oldHost); oldHost.Dispose();
    }

    internal bool RenderGpu()
    {
        if (!UseGpuRendering || !IsLoaded || ActualWidth < 100 || ActualHeight < 50 || DateTime.UtcNow < retryGpuAfter) return false;
        try
        {
            EnsureGpuHost();
            gpuHost!.Render(CreateGpuFrame());
            gpuFailure = null;
            return true;
        }
        catch (Exception e)
        {
            ReleaseGpu();
            gpuFailure = "GPU 연결 실패 · 기본 화면 표시 (" + e.Message + ")";
            retryGpuAfter = DateTime.UtcNow.AddSeconds(5);
            InvalidateVisual(); return false;
        }
    }

    internal GpuFrame CreateGpuFrame()
    {
        EnsureDrawings();
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        int height = Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY));
        const double gap = 8;
        double scale = Math.Min((ActualWidth - 6) / (PanelWidth * 2 + gap), (ActualHeight - 4) / PanelHeight);
        double x = (ActualWidth - (PanelWidth * 2 + gap) * scale) / 2;
        double y = (ActualHeight - PanelHeight * scale) / 2;
        if (backgroundPixels == null || backgroundWidth != width || backgroundHeight != height || !ReferenceEquals(backgroundFace, face))
        {
            // Rasterize the fixed labels and unlit face once per layout/style change; animated bars are drawn by the selected GPU.
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
                dc.PushTransform(new ScaleTransform(dpi.DpiScaleX, dpi.DpiScaleY));
                for (int channel = 0; channel < 2; channel++)
                {
                    dc.PushTransform(new TranslateTransform(x + channel * (PanelWidth + gap) * scale, y));
                    dc.PushTransform(new ScaleTransform(scale, scale));
                    dc.DrawDrawing(face);
                    Text(dc, channel == 0 ? "LEFT" : "RIGHT", 8, 3, 8.2, Printed);
                    dc.Pop(); dc.Pop();
                }
                dc.Pop();
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            backgroundPixels = new byte[checked(width * height * 4)];
            bitmap.CopyPixels(backgroundPixels, width * 4, 0);
            backgroundWidth = width; backgroundHeight = height; backgroundFace = face; backgroundVersion++;
        }
        gpuRectangles.Clear();
        for (int channel = 0; channel < 2; channel++)
        for (int band = 0; band < 8; band++)
        {
            int count = Math.Clamp((int)Math.Ceiling(levels[channel][band] * Steps - .025), 0, Steps);
            int peak = Math.Clamp((int)Math.Ceiling(peaks[channel][band] * Steps - .025), 0, Steps);
            void AddPair(Brush brush, double bx, double by, double bw, double thickness, double opacity)
            {
                var color = ((SolidColorBrush)brush).Color;
                uint argb = ((uint)Math.Round(color.A * opacity) << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
                float rx = (float)((x + (channel * (PanelWidth + gap) + bx) * scale) * dpi.DpiScaleX);
                float ry = (float)((y + by * scale) * dpi.DpiScaleY);
                float rw = (float)(bw * scale * dpi.DpiScaleX), rh = (float)(thickness * scale * dpi.DpiScaleY);
                gpuRectangles.Add(new(rx, ry, rw, rh, argb));
                gpuRectangles.Add(new(rx, ry + (float)(3 * scale * dpi.DpiScaleY), rw, rh, argb));
            }
            void AddSegment(int row, double opacity)
            {
                double bx = BandX(band), bw = BandWidth(band), by = RowY(row);
                AddPair(outerHalo, bx - 1, by - .6, bw + 2, 2.2, opacity);
                AddPair(halo, bx - .4, by - .25, bw + .8, 1.55, opacity);
                AddPair(lit, bx, by, bw, 1.05, opacity);
            }
            for (int row = 0; row < count; row++) AddSegment(row, 1);
            if (Settings.PeakHold && peak > count) AddSegment(peak - 1, .65);
        }
        return new GpuFrame(width, height, backgroundVersion, backgroundPixels, gpuRectangles);
    }

    private void EnsureDrawings()
    {
        if (face != null && Settings.Brightness == lastBrightness && Settings.Glow == lastGlow) return;
        lastBrightness = Settings.Brightness; lastGlow = Settings.Glow;
        lit = ColorBrush((byte)(255 * lastBrightness), 168, 226, 255);
        halo = ColorBrush((byte)(70 * lastGlow * lastBrightness), 78, 174, 251);
        outerHalo = ColorBrush((byte)(14 * lastGlow * lastBrightness), 44, 119, 224);
        face = new DrawingGroup();
        using (var dc = face.Open())
        {
            // The panel merges into the window: no outline, separator, glass layer or reflection.
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, PanelWidth, 145));
            for (int band = 0; band < 8; band++)
            {
                double bx = BandX(band), bw = BandWidth(band);
                for (int row = 0; row < Steps; row++)
                {
                    double by = RowY(row);
                    DrawPair(dc, Inactive, bx, by, bw, .65);
                    var segment = new DrawingGroup();
                    using (var s = segment.Open())
                    {
                        DrawPair(s, outerHalo, bx - 1, by - .6, bw + 2, 2.2);
                        DrawPair(s, halo, bx - .4, by - .25, bw + .8, 1.55);
                        DrawPair(s, lit, bx, by, bw, 1.05);
                    }
                    segment.Freeze(); activeSegments[band, row] = segment;
                }
                Text(dc, Frequencies[band], bx + bw / 2, 132, band == 7 ? 7.8 : 8.4, Printed, center: true);
            }
            string[] eqScale = ["+12", "+6", "0", "−6", "−12"];
            string[] spectrumScale = ["36", "27", "18", "9", "0"];
            for (int i = 0; i < 5; i++)
            {
                double cy = PlotTop + 2.3 + i * (PlotHeight - 4.6) / 4;
                Text(dc, eqScale[i], 26, cy - 5, 9.2, Dormant, right: true);
                Text(dc, spectrumScale[i], 330, cy - 5.4, 10.1, ScaleInk);
            }
            for (int i = 0; i < 9; i++)
            {
                double cy = PlotTop + 2.3 + i * (PlotHeight - 4.6) / 8;
                foreach (double tx in new[] { 32.0, 314.0 })
                {
                    dc.DrawRectangle(RedHalo, null, new Rect(tx - 1, cy - 1, 10, 3.2));
                    dc.DrawRectangle(RedCore, null, new Rect(tx, cy, 8, 1.2));
                }
            }
            Text(dc, "(dB)", 18, 132, 8.4, Printed, center: true);
            Text(dc, "(dB)", 340, 132, 8.4, Printed, center: true);
        }
        face.Freeze();
    }
    private static double BandX(int band) => band == 7 ? 276 : 50 + band * 31.5;
    private static double BandWidth(int band) => band == 7 ? 27.5 : 19.5;
    private static double RowY(int row) => PlotTop + PlotHeight - (row + 1) * (PlotHeight / Steps) + 1.5;
    private static void DrawPair(DrawingContext dc, Brush brush, double x, double y, double width, double thickness)
    {
        dc.DrawRectangle(brush, null, new Rect(x, y, width, thickness));
        dc.DrawRectangle(brush, null, new Rect(x, y + 3, width, thickness));
    }
    private void Text(DrawingContext dc, string text, double x, double y, double size, Brush brush, bool right = false, bool center = false)
    {
        string key = text + ":" + size.ToString("F2", CultureInfo.InvariantCulture) + ":" + brush;
        if (!textCache.TryGetValue(key, out var ft))
        {
            ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, lettering, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if (textCache.Count > 200) textCache.Clear(); textCache[key] = ft;
        }
        dc.DrawText(ft, new Point(x - (right ? ft.Width : center ? ft.Width / 2 : 0), y));
    }
    private static SolidColorBrush ColorBrush(byte a, byte r, byte g, byte b) { var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b)); brush.Freeze(); return brush; }
    private static SolidColorBrush Brush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
}
