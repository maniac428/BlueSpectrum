using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using BlueSpectrum.Audio;
using Microsoft.Win32;

namespace BlueSpectrum.UI;

public sealed class MainWindow : Window
{
    private readonly AppSettings settings;
    private readonly AudioCaptureService? capture;
    private readonly SpectrumPipeline? pipeline;
    private readonly DispatcherTimer timer;
    private readonly TextBlock status, format;
    private readonly Grid header, footer, layout;
    private readonly Button pinButton;
    private readonly double[] left = new double[7], right = new double[7];
    private long lastTick = Stopwatch.GetTimestamp();
    private int frameCount;
    private bool closed, fullScreen, focusMode = true;
    private readonly bool persistSettings;
    private WindowState savedState;
    private Rect savedBounds;
    internal SpectrumSurface Surface { get; }
    internal AppSettings Settings => settings;

    public MainWindow(bool startAudio = true, AppSettings? initialSettings = null, bool enableGpuRendering = false)
    {
        settings = initialSettings ?? AppSettings.Load(); settings.Validate(); persistSettings = startAudio;
        Title = "Blue Spectrum · SH-E70 FL";
        Width = settings.Width; Height = settings.Height; MinWidth = 720; MinHeight = 200;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        BlackWindowFrame.Attach(this);
        Topmost = settings.AlwaysOnTop;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (double.IsFinite(settings.Left) && double.IsFinite(settings.Top) &&
            settings.Left + settings.Width > SystemParameters.VirtualScreenLeft + 100 &&
            settings.Top + settings.Height > SystemParameters.VirtualScreenTop + 80 &&
            settings.Left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
            settings.Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80)
        { WindowStartupLocation = WindowStartupLocation.Manual; Left = settings.Left; Top = settings.Top; }

        layout = new Grid { Background = Brushes.Black };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        Content = layout;

        header = new Grid { Background = Brushes.Black };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new TextBlock { Text = "BLUE SPECTRUM   /   SH-E70 FL", Foreground = new SolidColorBrush(Color.FromRgb(144, 144, 144)), FontFamily = new FontFamily("Arial"), FontSize = 10, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(brand);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 4, 2) };
        Grid.SetColumn(buttons, 1); header.Children.Add(buttons);
        buttons.Children.Add(MakeButton("설정", (_, _) => OpenSettings(), "장치·밝기·움직임 설정 (Ctrl+,)"));
        pinButton = MakeButton(settings.AlwaysOnTop ? "고정됨" : "항상 위", (_, _) => TogglePin(), "다른 창 위에 표시 (Ctrl+T)"); buttons.Children.Add(pinButton);
        buttons.Children.Add(MakeButton("감상", (_, _) => ToggleFocus(), "두 표시창에 집중 (Ctrl+M). Esc로 복귀"));
        buttons.Children.Add(MakeButton("전체 화면", (_, _) => ToggleFullScreen(), "전체 화면 (F11). Esc로 복귀"));
        buttons.Children.Add(MakeButton("─", (_, _) => WindowState = WindowState.Minimized, "최소화"));
        buttons.Children.Add(MakeButton("×", (_, _) => Close(), "종료"));
        header.MouseLeftButtonDown += DragWindow; layout.Children.Add(header);

        Surface = new SpectrumSurface { Settings = settings, UseGpuRendering = startAudio || enableGpuRendering };
        Surface.NativeLeftClick = count =>
        {
            Activate(); Focus();
            if (count == 2) ToggleFullScreen();
            else if (!fullScreen && Mouse.LeftButton == MouseButtonState.Pressed) DragMove();
        };
        Grid.SetRow(Surface, 1); layout.Children.Add(Surface);
        Surface.MouseLeftButtonDown += DragWindow;

        footer = new Grid { Margin = new Thickness(9, 0, 9, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status = new TextBlock { Text = "출력 장치를 연결하는 중…", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        format = new TextBlock { Text = "WASAPI LOOPBACK", FontSize = 10, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(Color.FromRgb(119, 119, 119)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        footer.Children.Add(status); Grid.SetColumn(format, 1); footer.Children.Add(format); Grid.SetRow(footer, 2); layout.Children.Add(footer);

        var context = new ContextMenu();
        AddMenu(context, "설정…", () => OpenSettings());
        AddMenu(context, "항상 위에 표시 전환", TogglePin);
        AddMenu(context, "감상 모드 전환  Ctrl+M", ToggleFocus);
        AddMenu(context, "전체 화면 전환  F11", ToggleFullScreen);
        AddMenu(context, "기본 창으로 복귀  Esc", RestoreView);
        AddMenu(context, "종료", Close);
        layout.ContextMenu = context;
        KeyDown += OnKey;
        UpdateChrome();

        if (startAudio)
        {
            pipeline = new SpectrumPipeline(); capture = new AudioCaptureService();
            capture.StereoSamples += pipeline.Submit;
            capture.StreamReset += pipeline.Reset;
            capture.StatusChanged += message =>
            {
                if (!closed && !Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(() => { if (!closed) { status.Text = message; status.ToolTip = capture.DeviceName + "\n" + capture.FormatDescription; } });
            };
            Loaded += (_, _) => capture.Start(settings.DeviceId);
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1.0 / settings.Fps) };
        timer.Tick += Tick;
        if (startAudio) timer.Start();
        StateChanged += (_, _) => timer.Interval = WindowState == WindowState.Minimized ? TimeSpan.FromMilliseconds(300) : TimeSpan.FromSeconds(1.0 / settings.Fps);
        Closed += (_, _) =>
        {
            closed = true; timer.Stop(); SystemEvents.PowerModeChanged -= OnPowerModeChanged; capture?.Dispose(); pipeline?.Dispose(); Surface.ReleaseGpu();
            if (startAudio)
            {
                var r = fullScreen ? savedBounds : WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
                if (!r.IsEmpty) { settings.Width = r.Width; settings.Height = r.Height; settings.Left = r.Left; settings.Top = r.Top; }
                settings.Save();
            }
        };
    }

    private void Tick(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp(); double dt = Stopwatch.GetElapsedTime(lastTick, now).TotalSeconds; lastTick = now;
        if (WindowState == WindowState.Minimized) return;
        double leftFull = -100, rightFull = -100;
        bool fresh = pipeline?.Snapshot(left, right, out leftFull, out rightFull) ?? false;
        Surface.Update(left, right, dt, fresh, leftFull, rightFull);
        if (++frameCount % settings.Fps == 0 && capture != null)
        {
            format.Text = capture.IsRunning ? capture.FormatDescription + "  /  목표 " + settings.Fps + " FPS" : "WASAPI LOOPBACK";
            status.ToolTip = capture.DeviceName + "\n" + capture.FormatDescription;
        }
    }
    private static Button MakeButton(string label, RoutedEventHandler handler, string tooltip)
    {
        var b = new Button { Content = label, ToolTip = tooltip, Padding = new Thickness(11, 2, 11, 3), Margin = new Thickness(3, 0, 0, 0), FontSize = 11 };
        b.Click += handler; return b;
    }
    private static void AddMenu(ContextMenu menu, string label, Action action) { var i = new MenuItem { Header = label }; i.Click += (_, _) => action(); menu.Items.Add(i); }
    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject obj)
        {
            for (var d = obj; d != null; d = VisualTreeHelper.GetParent(d)) if (d is Button) return;
        }
        if (e.ClickCount == 2) ToggleFullScreen();
        else if (!fullScreen && e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void TogglePin() { settings.AlwaysOnTop = !settings.AlwaysOnTop; Topmost = settings.AlwaysOnTop; pinButton.Content = Topmost ? "고정됨" : "항상 위"; settings.Save(); }
    internal void ToggleFocus() { focusMode = !focusMode; UpdateChrome(); }
    internal void ToggleFullScreen()
    {
        if (!fullScreen)
        {
            savedState = WindowState; savedBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            fullScreen = true; WindowState = WindowState.Normal; ResizeMode = ResizeMode.NoResize; FullScreenPlacement.FillMonitor(this);
        }
        else
        {
            fullScreen = false; WindowState = WindowState.Normal; ResizeMode = ResizeMode.CanResize;
            Left = savedBounds.Left; Top = savedBounds.Top; Width = savedBounds.Width; Height = savedBounds.Height; WindowState = savedState;
        }
        UpdateChrome();
    }
    internal void RestoreView() { if (fullScreen) ToggleFullScreen(); focusMode = false; UpdateChrome(); }
    private void UpdateChrome()
    {
        bool hidden = focusMode || fullScreen;
        header.Visibility = footer.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        layout.RowDefinitions[0].Height = new GridLength(hidden ? 0 : 28);
        layout.RowDefinitions[2].Height = new GridLength(hidden ? 0 : 18);
    }
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) RestoreView();
        else if (e.Key == Key.F11) ToggleFullScreen();
        else if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.T) TogglePin(); else if (e.Key == Key.M) ToggleFocus(); else if (e.Key == Key.OemComma) OpenSettings();
        }
    }
    private void OpenSettings()
    {
        var oldDevice = settings.DeviceId;
        var oldGpu = settings.RenderGpuId;
        var dialog = new OptionsWindow(settings, MoveToPosition, Surface.RendererStatus) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        settings.Validate(); Topmost = settings.AlwaysOnTop; pinButton.Content = Topmost ? "고정됨" : "항상 위";
        timer.Interval = TimeSpan.FromSeconds(1.0 / settings.Fps);
        if (oldDevice != settings.DeviceId) { pipeline?.Reset(); capture?.Start(settings.DeviceId); }
        if (oldGpu != settings.RenderGpuId || Surface.GpuHost?.Renderer == null || Surface.GpuHost.Renderer.RequestedAdapterMissing)
            Surface.ApplyGpuSelection();
        Surface.InvalidateVisual();
        if (!settings.Save()) status.Text = "설정을 저장하지 못했습니다. 쓰기 가능한 폴더에서 실행해 주세요.";
    }
    internal void SetDiagnosticCaption(string text) { status.Text = text; format.Text = "SYNTHETIC INPUT / VISUAL CHECK"; }
    internal bool MoveToPosition(WindowPosition position)
    {
        // Capture the current monitor before restoring fullscreen/maximized bounds.
        var workArea = WindowPositioning.GetWorkArea(this);
        if (workArea.IsEmpty) return false;
        if (fullScreen) ToggleFullScreen();
        if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
        UpdateLayout();
        if (!WindowPositioning.Move(this, position, workArea)) return false;
        settings.Left = Left; settings.Top = Top; settings.Width = ActualWidth; settings.Height = ActualHeight;
        if (persistSettings && !settings.Save()) status.Text = "창은 이동했지만 위치를 저장하지 못했습니다.";
        return true;
    }
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && !closed && !Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(() => { if (!closed) { pipeline?.Reset(); capture?.Start(settings.DeviceId); Surface.ApplyGpuSelection(); } });
    }
}
