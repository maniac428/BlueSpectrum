using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlueSpectrum.Audio;
using BlueSpectrum.Dsp;
using BlueSpectrum.Tests;
using BlueSpectrum.UI;
using BlueSpectrum.Graphics;

namespace BlueSpectrum;

internal static class Diagnostics
{
    internal static int Run(string[] args)
    {
        string output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(AppContext.BaseDirectory, "diagnostics.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (args[0] == "--self-test")
            {
                Write(output, new { Passed = true, Dsp = DspTests.Run(), Decoder = AudioDecoderTests.Run(), Pipeline = PipelineTests.Run().GetAwaiter().GetResult(), Note = "Synthetic tests only; WASAPI integration is tested separately." });
            }
            else if (args[0] == "--probe") Probe(output);
            else if (args[0] == "--device-test") DeviceTest(output);
            else if (args[0] == "--ui-test") UiTest(output);
            else if (args[0] == "--gpu-test") GpuTest(output);
            else if (args[0] == "--loopback-test") Write(output, LiveLoopbackTests.Run(args.Length > 2 ? args[2] : null));
            else if (args[0] == "--render")
            {
                int w = args.Length > 2 ? int.Parse(args[2]) : 800, h = args.Length > 3 ? int.Parse(args[3]) : 214;
                Render(output, w, h);
            }
            else if (args[0] == "--render-options") RenderControls(output, false);
            else if (args[0] == "--render-menu") RenderControls(output, true);
            else throw new ArgumentException("지원 명령: --self-test, --probe, --device-test, --ui-test, --render [출력경로] [폭] [높이]");
            return 0;
        }
        catch (Exception e)
        {
            Write(output.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? output + ".error.json" : output, new { Passed = false, Error = e.ToString() });
            return 1;
        }
    }
    internal static void Write(string path, object report) => File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

    private static void DeviceTest(string output)
    {
        using var capture = new AudioCaptureService();
        var checks = new List<object>();
        foreach (var device in AudioCaptureService.EnumerateDevices())
        {
            capture.Start(device.Id); Thread.Sleep(1300);
            checks.Add(new { Requested = device.Name, capture.DeviceName, capture.IsRunning, capture.FormatDescription, Match = device.Id == capture.ActiveDeviceId });
            capture.Stop();
        }
        capture.Start("BlueSpectrum-deliberately-missing-device"); Thread.Sleep(1200);
        bool missingWaits = !capture.IsRunning && capture.ActiveDeviceId == null;
        capture.Start(); Thread.Sleep(1300);
        Write(output, new { Checks = checks, MissingDeviceWaits = missingWaits, DefaultRecovered = capture.IsRunning, DefaultDevice = capture.DeviceName, Note = "Only app capture selections changed. Windows default/volume unchanged. Physical unplug and suspend were not performed." });
    }

    private static void UiTest(string output)
    {
        var app = new Application(); Program.ApplyTheme(app);
        var settings = new AppSettings();
        var positionChecks = new List<object>();
        var window = new MainWindow(false, settings) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        void VerifyChrome(bool hidden)
        {
            var grid = (System.Windows.Controls.Grid)window.Content;
            var chrome = grid.Children.OfType<FrameworkElement>().Where(e => e != window.Surface).ToArray();
            if (chrome.Length != 2 || chrome.Any(e => e.Visibility != (hidden ? Visibility.Collapsed : Visibility.Visible)) ||
                grid.RowDefinitions[0].Height.Value != (hidden ? 0 : 28) || grid.RowDefinitions[2].Height.Value != (hidden ? 0 : 18))
                throw new InvalidOperationException("Startup focus mode or chrome restoration failed");
        }
        VerifyChrome(true);
        app.DispatcherUnhandledException += (_, e) => { Write(output, new { Passed = false, Error = e.Exception.ToString() }); e.Handled = true; app.Shutdown(1); };
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                VerifyChrome(true);
                window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(window)!, Environment.TickCount, System.Windows.Input.Key.Escape)
                    { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
                VerifyChrome(false);
                window.ToggleFocus(); VerifyChrome(true); window.RestoreView(); VerifyChrome(false);
                window.ToggleFullScreen(); window.RestoreView();
                bool? mainBorderSuppressed = BlackWindowFrame.BorderSuppressionAccepted(window), optionsBorderSuppressed = null;
                var menu = ((FrameworkElement)window.Content).ContextMenu;
                menu.Opacity = 0; menu.PlacementTarget = window; menu.IsOpen = true; menu.UpdateLayout();
                if (!menu.IsOpen || menu.ActualWidth < 20) throw new InvalidOperationException("Context menu failed to open");
                menu.IsOpen = false;
                var options = new OptionsWindow(settings, window.MoveToPosition) { Owner = window, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                options.Loaded += (_, _) => options.Dispatcher.BeginInvoke(() =>
                {
                    optionsBorderSuppressed = BlackWindowFrame.BorderSuppressionAccepted(options);
                    var moveButtons = Find<System.Windows.Controls.Button>(options).Where(b => b.Tag is WindowPosition).ToList();
                    if (moveButtons.Count != 5) throw new InvalidOperationException("Five window position buttons missing");
                    foreach (var button in moveButtons)
                    {
                        var position = (WindowPosition)button.Tag;
                        var work = WindowPositioning.GetWorkArea(window);
                        var before = WindowPositioning.GetBounds(window);
                        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        var after = WindowPositioning.GetBounds(window);
                        bool centered = position == WindowPosition.Center;
                        bool leftSide = position is WindowPosition.TopLeft or WindowPosition.BottomLeft;
                        bool topSide = position is WindowPosition.TopLeft or WindowPosition.TopRight;
                        double horizontalError = centered ? after.Left + after.Width / 2 - (work.Left + work.Width / 2)
                            : leftSide ? after.Left - work.Left : after.Right - work.Right;
                        double verticalError = centered ? after.Top + after.Height / 2 - (work.Top + work.Height / 2)
                            : topSide ? after.Top - work.Top : after.Bottom - work.Bottom;
                        if (Math.Abs(horizontalError) > 1 || Math.Abs(verticalError) > 1 || before.Size != after.Size)
                            throw new InvalidOperationException("Position or size mismatch: " + position);
                        positionChecks.Add(new { Position = position.ToString(), WorkArea = work.ToString(), Bounds = after.ToString(), SizePreserved = true });
                    }
                    // Position changes must persist in the settings model even when this dialog is later cancelled.
                    if (settings.Left != window.Left || settings.Top != window.Top) throw new InvalidOperationException("Position settings did not follow the window");
                    window.ToggleFullScreen();
                    moveButtons.Single(b => (WindowPosition)b.Tag == WindowPosition.Center).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    if (window.ResizeMode != ResizeMode.CanResize || window.WindowState != WindowState.Normal)
                        throw new InvalidOperationException("Placement did not restore fullscreen to a normal window");
                    var renderGpu = Find<System.Windows.Controls.ComboBox>(options).Single(b => b.Name == "RenderGpuSelector");
                    if (renderGpu.Items.Count < 1 || renderGpu.SelectedValue as string != "") throw new InvalidOperationException("GPU automatic option missing");
                    renderGpu.ApplyTemplate();
                    var gpuPopup = renderGpu.Template.FindName("PART_Popup", renderGpu) as System.Windows.Controls.Primitives.Popup;
                    if (gpuPopup?.Child == null) throw new InvalidOperationException("GPU dropdown popup missing");
                    gpuPopup.Child.Opacity = 0; renderGpu.IsDropDownOpen = true; renderGpu.UpdateLayout();
                    if (!gpuPopup.IsOpen) throw new InvalidOperationException("GPU dropdown failed to open");
                    if (renderGpu.Items.Count > 1) renderGpu.SelectedIndex = 1;
                    renderGpu.IsDropDownOpen = false;
                    var devices = Find<System.Windows.Controls.ComboBox>(options).Single(b => b.Name == "AudioDeviceSelector");
                    devices.ApplyTemplate();
                    var popup = devices.Template.FindName("PART_Popup", devices) as System.Windows.Controls.Primitives.Popup;
                    if (popup?.Child == null) throw new InvalidOperationException("Device dropdown popup missing");
                    popup.Child.Opacity = 0; devices.IsDropDownOpen = true; devices.UpdateLayout();
                    if (!popup.IsOpen) throw new InvalidOperationException("Device dropdown failed to open");
                    int initialIndex = devices.SelectedIndex;
                    if (devices.Items.Count > 1) devices.SelectedIndex = 1;
                    devices.IsDropDownOpen = false; devices.SelectedIndex = initialIndex;
                    var sliders = Find<System.Windows.Controls.Slider>(options).ToList();
                    sliders[0].Value = 12;
                    Find<System.Windows.Controls.Button>(options).Single(b => b.Content as string == "적용").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                });
                bool? result = options.ShowDialog();
                if (result != true || settings.GainDb != 12) throw new InvalidOperationException("Settings dialog apply failed");
                var selectedGpu = Find<System.Windows.Controls.ComboBox>(options).Single(b => b.Name == "RenderGpuSelector").SelectedValue as string;
                if (settings.RenderGpuId != (string.IsNullOrEmpty(selectedGpu) ? null : selectedGpu)) throw new InvalidOperationException("GPU selection not applied");
                var surface = window.Surface; var db = Enumerable.Repeat(-12.0, 7).ToArray();
                for (int i = 0; i < 100; i++) surface.Update(db, db, 1 / 60.0, true);
                for (int i = 0; i < 240; i++) surface.Update(db, db, 1 / 60.0, false);
                window.Width = 720; window.Height = 200; window.UpdateLayout();
                Write(output, new { Passed = true, PositionChecks = positionChecks, MainBorderSuppressionAccepted = mainBorderSuppressed, OptionsBorderSuppressionAccepted = optionsBorderSuppressed, Checks = new[] { "WPF HWND initialization", "Focus mode active before first show and after Loaded", "Escape key routed event restores menu and footer", "Focus mode round trip", "Fullscreen round trip", "Five actual position button events and physical work-area bounds", "Position buttons preserve size and update saved position model", "Position button exits fullscreen", "Black context menu opens", "Black device dropdown opens and selects an item", "Invisible settings dialog with actual Apply event", "Settings gain value updated", "Live-to-silence render update", "Resize to minimum" }, Scope = "In-process WPF UI integration. Test windows and popups transparent and not activated. Native border fields report DWM setter acceptance. Physical keyboard/mouse/DPI-change not exercised." });
            }
            catch (Exception e) { Write(output, new { Passed = false, Error = e.ToString() }); }
            finally { window.Close(); }
        });
        app.Run(window);
    }
    private static void GpuTest(string output)
    {
        var app = new Application(); Program.ApplyTheme(app);
        var settings = new AppSettings { Width = 800, Height = 200 };
        var window = new MainWindow(false, settings, enableGpuRendering: true) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        app.DispatcherUnhandledException += (_, e) => { Write(output, new { Passed = false, Error = e.Exception.ToString() }); e.Handled = true; app.Shutdown(1); };
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var adapters = GpuCatalog.Enumerate();
                if (adapters.Count == 0) throw new InvalidOperationException("No hardware GPU enumerated");
                var checks = new List<object>();
                var left = Enumerable.Repeat(-12.0, 7).ToArray();
                var right = Enumerable.Repeat(-100.0, 7).ToArray();
                foreach (var adapter in adapters)
                {
                    settings.RenderGpuId = adapter.Id;
                    window.Surface.ApplyGpuSelection(); window.UpdateLayout();
                    for (int i = 0; i < 100; i++) window.Surface.Update(left, right, 1 / 60.0, true, -6, -100);
                    var renderer = window.Surface.GpuHost?.Renderer ?? throw new InvalidOperationException(window.Surface.RendererStatus);
                    if (renderer.ActiveAdapter.Id != adapter.Id) throw new InvalidOperationException("Actual device adapter did not match selection");
                    var frame = window.Surface.CreateGpuFrame();
                    byte[] pixels = renderer.ReadPixels();
                    if (pixels.Length != frame.Width * frame.Height * 4) throw new InvalidOperationException("Readback size mismatch");
                    int leftLit = 0, rightLit = 0;
                    for (int y = 0; y < frame.Height; y++)
                    for (int x = 0; x < frame.Width; x++)
                    {
                        int p = (y * frame.Width + x) * 4;
                        if (pixels[p] > 150 && pixels[p + 1] > 100 && pixels[p] > pixels[p + 2] + 25)
                        { if (x < frame.Width / 2) leftLit++; else rightLit++; }
                    }
                    if (leftLit < rightLit + 500) throw new InvalidOperationException("GPU output did not show independently lit left-channel bars");
                    string preview = Path.Combine(Path.GetDirectoryName(output)!, "gpu-" + checks.Count + "-v1.7.png");
                    var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, pixels, frame.Width * 4);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(preview)) encoder.Save(stream);
                    window.Width = 940; window.Height = 250; window.UpdateLayout();
                    if (!window.Surface.RenderGpu()) throw new InvalidOperationException("GPU resize failed: " + window.Surface.RendererStatus);
                    var resized = window.Surface.CreateGpuFrame();
                    if (renderer.ReadPixels().Length != resized.Width * resized.Height * 4) throw new InvalidOperationException("GPU resized readback size mismatch");
                    checks.Add(new { Requested = adapter, Actual = renderer.ActiveAdapter, renderer.ActiveAdapterLuid, renderer.RenderedFrameCount, LeftLitPixels = leftLit, RightLitPixels = rightLit, ResizePassed = true, Preview = Path.GetFileName(preview) });
                    window.Width = 800; window.Height = 200; window.UpdateLayout();
                }
                settings.RenderGpuId = "missing-gpu-for-diagnostic";
                window.Surface.ApplyGpuSelection(); window.UpdateLayout();
                window.Surface.RenderGpu();
                string missingStatus = window.Surface.RendererStatus;
                if (!missingStatus.Contains("없") && !missingStatus.Contains("실패") && !missingStatus.Contains("연결되지"))
                    throw new InvalidOperationException("Missing GPU was not clearly reported: " + missingStatus);
                settings.RenderGpuId = null; window.Surface.ApplyGpuSelection(); window.UpdateLayout();
                if (!window.Surface.RenderGpu() || window.Surface.GpuHost?.Renderer == null) throw new InvalidOperationException("Automatic GPU recovery failed");
                window.Surface.GpuHost.Renderer.Dispose();
                if (window.Surface.RenderGpu() || window.Surface.GpuHost != null || !window.Surface.RendererStatus.Contains("실패"))
                    throw new InvalidOperationException("GPU failure did not recover the WPF fallback");
                window.Surface.ApplyGpuSelection(); window.UpdateLayout();
                if (!window.Surface.RenderGpu()) throw new InvalidOperationException("Device recreation failed");
                window.ToggleFullScreen(); window.UpdateLayout();
                if (!window.Surface.RenderGpu()) throw new InvalidOperationException("GPU fullscreen failed");
                window.RestoreView(); window.UpdateLayout();
                if (!window.Surface.RenderGpu()) throw new InvalidOperationException("GPU restore failed");
                var menu = ((FrameworkElement)window.Content).ContextMenu;
                menu.Opacity = 0;
                window.Surface.GpuHost!.RightClick?.Invoke();
                if (!menu.IsOpen) throw new InvalidOperationException("GPU surface context menu failed");
                menu.IsOpen = false;
                window.ToggleFocus(); window.UpdateLayout(); window.Surface.RenderGpu();
                var host = window.Surface.GpuHost!;
                var parentHandle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var bounds = WindowPositioning.GetBounds(window);
                var resizeChecks = new List<object>();
                foreach (var edge in new[] { new Point(bounds.Left + 1, bounds.Top + bounds.Height / 2), new Point(bounds.Right - 2, bounds.Top + bounds.Height / 2),
                    new Point(bounds.Left + bounds.Width / 2, bounds.Top + 1), new Point(bounds.Left + bounds.Width / 2, bounds.Bottom - 2) })
                {
                    nint coords = unchecked((nint)((((uint)(int)edge.Y & 0xffff) << 16) | ((uint)(int)edge.X & 0xffff)));
                    var parentHit = NativeDiagnosticMessage(parentHandle, 0x0084, 0, coords);
                    var childHit = NativeDiagnosticMessage(host.HostHandle, 0x0084, 0, coords);
                    if (parentHit < 10 || parentHit > 17 || childHit != -1) throw new InvalidOperationException("GPU child blocked window resize edge");
                    resizeChecks.Add(new { X = edge.X, Y = edge.Y, ParentHit = (long)parentHit, ChildHit = (long)childHit });
                }
                var clickEvents = new List<int>();
                var originalClick = host.LeftClick;
                host.LeftClick = clickEvents.Add;
                NativeDiagnosticMessage(host.HostHandle, 0x0201, 0, 0);
                NativeDiagnosticMessage(host.HostHandle, 0x0203, 0, 0);
                host.LeftClick = originalClick;
                if (!clickEvents.SequenceEqual(new[] { 1, 2 })) throw new InvalidOperationException("GPU native click forwarding failed");
                NativeDiagnosticMessage(host.HostHandle, 0x0205, 0, 0);
                if (!menu.IsOpen) throw new InvalidOperationException("GPU native right click failed");
                menu.IsOpen = false;
                window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(window)!, Environment.TickCount, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
                if (((System.Windows.Controls.Grid)window.Content).RowDefinitions[0].Height.Value != 28) throw new InvalidOperationException("Escape restoration with GPU child failed");
                Write(output, new { Passed = true, Adapters = checks, MissingSelectionStatus = missingStatus, AutomaticStatus = window.Surface.RendererStatus,
                    ResizeEdges = resizeChecks, NativeClickEvents = clickEvents,
                    Checks = new[] { "Explicit hardware adapter creation and actual D3D11 adapter identity", "GPU texture readback and independent left/right lit bars", "Live adapter switching", "Resize and fullscreen restoration", "Missing selection fallback and automatic recovery", "Disposed device WPF fallback and device recreation", "GPU surface native context menu", "Native hit tests pass all four resize edges through", "Native single/double click forwarding and Escape restoration" },
                    Scope = "Synthetic input rendered by real GPUs in nonactivated transparent test windows. Static face is cached WPF raster; dynamic bars are Direct2D on selected D3D11 device. CPU FFT unchanged. No real-time FPS/power benchmark or physical unplug test." });
            }
            catch (Exception e) { Write(output, new { Passed = false, Error = e.ToString() }); }
            finally { window.Close(); }
        });
        app.Run(window);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint NativeDiagnosticMessage(nint hwnd, int msg, nint wParam, nint lParam);

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T result) yield return result;
            foreach (var found in Find<T>(child)) yield return found;
        }
    }

    private static void Probe(string output)
    {
        using var capture = new AudioCaptureService();
        using var pipeline = new SpectrumPipeline();
        var messages = new List<string>(); var gate = new object(); long packets = 0; float peak = 0;
        capture.StatusChanged += s => { lock (gate) messages.Add(s); };
        capture.StreamReset += pipeline.Reset;
        capture.StereoSamples += (data, rate) =>
        {
            pipeline.Submit(data, rate); Interlocked.Increment(ref packets);
            foreach (float sample in data) peak = Math.Max(peak, Math.Abs(sample));
        };
        var devices = AudioCaptureService.EnumerateDevices();
        var watch = Stopwatch.StartNew(); capture.Start(); Thread.Sleep(7000);
        var left = new double[7]; var right = new double[7]; bool fresh = pipeline.Snapshot(left, right, out double leftFull, out double rightFull);
        var report = new { Mode = "Read-only actual WASAPI capture", Devices = devices, capture.DeviceName, capture.FormatDescription, capture.ActiveDeviceId, capture.IsRunning, capture.TotalFramesCaptured, Packets = Interlocked.Read(ref packets), PeakAbs = peak, HasRecentPacket = fresh, LeftDb = left, RightDb = right, LeftFullRangeDb = leftFull, RightFullRangeDb = rightFull, Messages = messages.ToArray(), Seconds = watch.Elapsed.TotalSeconds };
        capture.Stop(); Write(output, report);
    }

    private static void Render(string output, int width, int height)
    {
        var app = new Application(); Program.ApplyTheme(app);
        var window = new MainWindow(false, new AppSettings { Width = width, Height = height });
        window.RestoreView(); // Keep the synthetic-input caption visible in diagnostic previews.
        window.SetDiagnosticCaption("검증용 합성 입력 · 실시간 화면 아님");
        // Deterministic multi-tone fixtures pass through the real analyzer, never live mode.
        var analyzer = new StereoSpectrumAnalyzer(48000);
        var samples = new float[48000 * 2];
        double[] a = [.1, .09, .055, .07, .026, .013, .006], b = [.035, .055, .09, .04, .055, .029, .014];
        for (int i = 0; i < 48000; i++)
            for (int k = 0; k < 7; k++)
            {
                var sin = Math.Sin(2 * Math.PI * StereoSpectrumAnalyzer.Centers[k] * i / 48000);
                samples[2 * i] += (float)(a[k] * sin); samples[2 * i + 1] += (float)(b[k] * sin);
            }
        analyzer.AddFrames(samples);
        for (int i = 0; i < 100; i++) window.Surface.Update(analyzer.LeftDb, analyzer.RightDb, 1 / 60.0, true, analyzer.LeftFullRangeDb, analyzer.RightFullRangeDb);
        var content = (FrameworkElement)window.Content;
        RenderElement(output, content, width, height);
        window.Close(); app.Shutdown();
    }
    private static void RenderControls(string output, bool contextMenu)
    {
        var app = new Application(); Program.ApplyTheme(app);
        if (contextMenu)
        {
            var window = new MainWindow(false);
            var menu = ((FrameworkElement)window.Content).ContextMenu;
            RenderElement(output, menu, 285, 270);
            window.Close();
        }
        else
        {
            var options = new OptionsWindow(new AppSettings(), _ => true);
            RenderElement(output, (FrameworkElement)options.Content, 540, 640);
            options.Close();
        }
        app.Shutdown();
    }
    private static void RenderElement(string output, FrameworkElement content, int width, int height)
    {
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen()) dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(background); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(output); encoder.Save(stream);
    }
}
