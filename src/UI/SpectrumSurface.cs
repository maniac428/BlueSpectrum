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
    private const int Steps = 26;
    private const double PanelWidth = 354, PanelHeight = 115, PanelGap = 0, PlotTop = 24, PlotHeight = 60;
    private const double SegmentHeight = 1.3;
    private static readonly string[] Frequencies = ["63Hz", "160Hz", "400Hz", "1kHz", "2.5kHz", "6.3kHz", "16kHz", "full range"];
    private readonly double[][] levels = [new double[8], new double[8]];
    private readonly double[][] peaks = [new double[8], new double[8]];
    private readonly double[][] holds = [new double[8], new double[8]];
    private readonly Dictionary<string, FormattedText> textCache = new();
    private readonly Typeface lettering = new("Arial");
    private DrawingGroup? face;
    private readonly DrawingGroup[,] activeSegments = new DrawingGroup[8, Steps];
    private Brush lit = Brushes.LightBlue, hotCore = Brushes.White, halo = Brushes.Blue, outerHalo = Brushes.Blue;
    private static readonly Brush Inactive = Brush("#02080D"), MarkerHalo = ColorBrush(70, 35, 125, 245), MarkerCore = ColorBrush(238, 155, 232, 255), Printed = Brush("#909DA5");
    private static readonly Brush TickInk = Brush("#909DA5");
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
    internal double DisplayLevel(int channel, int band) => levels[channel][band];
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
        double scale = Math.Min((ActualWidth - 6) / (PanelWidth * 2 + PanelGap), (ActualHeight - 4) / PanelHeight);
        if (scale <= 0) return;
        double x = (ActualWidth - (PanelWidth * 2 + PanelGap) * scale) / 2;
        double y = (ActualHeight - PanelHeight * scale) / 2;
        for (int channel = 0; channel < 2; channel++)
        {
            dc.PushTransform(new TranslateTransform(x + channel * (PanelWidth + PanelGap) * scale, y));
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
            Text(dc, channel == 0 ? "left channel" : "right channel", 177, 3, 8.2, Printed, center: true);
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
        double scale = Math.Min((ActualWidth - 6) / (PanelWidth * 2 + PanelGap), (ActualHeight - 4) / PanelHeight);
        double x = (ActualWidth - (PanelWidth * 2 + PanelGap) * scale) / 2;
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
                    dc.PushTransform(new TranslateTransform(x + channel * (PanelWidth + PanelGap) * scale, y));
                    dc.PushTransform(new ScaleTransform(scale, scale));
                    dc.DrawDrawing(face);
                    Text(dc, channel == 0 ? "left channel" : "right channel", 177, 3, 8.2, Printed, center: true);
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
            void AddCell(Brush brush, double bx, double by, double bw, double cellHeight, double opacity)
            {
                var color = ((SolidColorBrush)brush).Color;
                uint argb = ((uint)Math.Round(color.A * opacity) << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
                float rx = (float)((x + (channel * (PanelWidth + PanelGap) + bx) * scale) * dpi.DpiScaleX);
                float ry = (float)((y + by * scale) * dpi.DpiScaleY);
                float rw = (float)(bw * scale * dpi.DpiScaleX), rh = (float)(cellHeight * scale * dpi.DpiScaleY);
                gpuRectangles.Add(new(rx, ry, rw, rh, argb));
            }
            void AddSegment(int row, double opacity)
            {
                double bx = BandX(band), bw = BandWidth(band), by = RowY(row);
                AddCell(outerHalo, bx - 2.8, by - .9, bw + 5.6, SegmentHeight + 1.8, opacity);
                AddCell(halo, bx - 1.2, by - .35, bw + 2.4, SegmentHeight + .7, opacity);
                AddCell(lit, bx, by, bw, SegmentHeight, opacity);
                AddCell(hotCore, bx + .8, by + .3, bw - 1.6, .65, opacity);
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
        lit = ColorBrush((byte)(230 * lastBrightness), 100, 205, 240);
        hotCore = ColorBrush((byte)(225 * lastBrightness), 215, 248, 252);
        halo = ColorBrush((byte)(130 * lastGlow * lastBrightness), 48, 152, 255);
        outerHalo = ColorBrush((byte)(42 * lastGlow * lastBrightness), 28, 99, 230);
        face = new DrawingGroup();
        using (var dc = face.Open())
        {
            // The panel merges into the window: no outline, separator, glass layer or reflection.
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, PanelWidth, PanelHeight));
            for (int band = 0; band < 8; band++)
            {
                double bx = BandX(band), bw = BandWidth(band);
                for (int row = 0; row < Steps; row++)
                {
                    double by = RowY(row);
                    dc.DrawRectangle(Inactive, null, new Rect(bx, by, bw, SegmentHeight));
                    var segment = new DrawingGroup();
                    using (var s = segment.Open())
                    {
                        s.DrawRectangle(outerHalo, null, new Rect(bx - 2.8, by - .9, bw + 5.6, SegmentHeight + 1.8));
                        s.DrawRectangle(halo, null, new Rect(bx - 1.2, by - .35, bw + 2.4, SegmentHeight + .7));
                        s.DrawRectangle(lit, null, new Rect(bx, by, bw, SegmentHeight));
                        s.DrawRectangle(hotCore, null, new Rect(bx + .8, by + .3, bw - 1.6, .65));
                    }
                    segment.Freeze(); activeSegments[band, row] = segment;
                }
                Text(dc, Frequencies[band], bx + bw / 2, 99, band == 7 ? 7.6 : 8.4, Printed, center: true);
            }
            string[] eqScale = ["+12", "+6", "0", "−6", "−12"];
            string[] spectrumScale = ["36", "27", "18", "9", "0"];
            for (int i = 0; i < 5; i++)
            {
                double cy = PlotTop + 2.3 + i * (PlotHeight - 4.6) / 4;
                Text(dc, eqScale[i], 26, cy - 5, 9.2, Printed, right: true);
                Text(dc, spectrumScale[i], 330, cy - 5, 9.2, Printed);
            }
            for (int i = 0; i < 13; i++)
            {
                double cy = PlotTop + 2.3 + i * (PlotHeight - 4.6) / 12;
                double width = i % 3 == 0 ? 8 : 4;
                dc.DrawRectangle(TickInk, null, new Rect(36 - width, cy, width, .7));
                dc.DrawRectangle(TickInk, null, new Rect(320, cy, width, .7));
            }
            // SH-8057 has five always-visible reference dots in vertical columns between the eight bars.
            double[] markerCenters = new double[9];
            markerCenters[0] = BandCenter(0) - (BandCenter(1) - BandCenter(0)) / 2;
            for (int i = 0; i < 8; i++) markerCenters[i + 1] = 2 * BandCenter(i) - markerCenters[i];
            foreach (double mx in markerCenters)
            for (int i = 0; i < 5; i++)
            {
                double my = PlotTop + 2.3 + i * (PlotHeight - 4.6) / 4;
                dc.DrawRectangle(MarkerHalo, null, new Rect(mx - 3.5, my - 1.4, 7, 4.6));
                dc.DrawRectangle(MarkerCore, null, new Rect(mx - 2, my, 4, 1.8));
            }
            Text(dc, "(dB)", 26, 13, 7.2, Printed, right: true);
            Text(dc, "(dB)", 330, 13, 7.2, Printed);
            Text(dc, "equalizer", 5, 99, 7.6, Printed);
            Text(dc, "spectrum", 350, 99, 7.6, Printed, right: true);
            dc.DrawRectangle(Printed, null, new Rect(50, 8, 98, .55));
            dc.DrawRectangle(Printed, null, new Rect(207, 8, 103, .55));
        }
        face.Freeze();
    }
    private static double BandX(int band) => band == 7 ? 269 : 50 + band * 31.5;
    private static double BandWidth(int band) => band == 7 ? 26 : 13;
    private static double BandCenter(int band) => BandX(band) + BandWidth(band) / 2;
    private static double RowY(int row) => PlotTop + 2.3 + (Steps - 1 - row) * (PlotHeight - 4.6) / (Steps - 1) - SegmentHeight / 2;
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
