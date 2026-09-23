using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Shell;
using BlueSpectrum.Audio;
using BlueSpectrum.Graphics;

namespace BlueSpectrum.UI;

internal sealed class OptionsWindow : Window
{
    public OptionsWindow(AppSettings settings, Func<WindowPosition, bool>? moveWindow = null, string? rendererStatus = null)
    {
        Title = "Blue Spectrum 설정"; Width = 540; Height = 640; MinHeight = 460;
        ResizeMode = ResizeMode.CanResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        BlackWindowFrame.Attach(this);
        var outer = new DockPanel { Margin = new Thickness(20) };
        var titleBar = new DockPanel { Background = Brushes.Black, Margin = new Thickness(0, 0, 0, 12), Height = 26 };
        var close = new Button { Content = "×", IsCancel = true, Padding = new Thickness(12, 0, 0, 0), ToolTip = "설정 닫기" };
        DockPanel.SetDock(close, Dock.Right); titleBar.Children.Add(close);
        titleBar.Children.Add(new TextBlock { Text = Title, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            for (DependencyObject? d = e.OriginalSource as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
                if (d is Button) return;
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        };
        DockPanel.SetDock(titleBar, Dock.Top); outer.Children.Add(titleBar);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "취소", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "적용", IsDefault = true, MinWidth = 90 };
        actions.Children.Add(cancel); actions.Children.Add(save); DockPanel.SetDock(actions, Dock.Bottom); outer.Children.Add(actions);
        var body = new StackPanel(); outer.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = outer;
        body.Children.Add(new TextBlock { Text = "표시와 오디오 입력", FontSize = 22, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 20) });
        body.Children.Add(Label("창 위치"));
        var positions = new Grid { Margin = new Thickness(0, 2, 0, 3) };
        for (int column = 0; column < 3; column++) positions.ColumnDefinitions.Add(new ColumnDefinition());
        for (int row = 0; row < 2; row++) positions.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        var positionNote = new TextBlock { Text = "현재 모니터 · 작업표시줄 제외 · 누르면 바로 이동·저장", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 9), TextWrapping = TextWrapping.Wrap };
        void AddPosition(string label, WindowPosition position, int row, int column, int rows = 1)
        {
            var button = new Button { Content = label, Tag = position, Padding = new Thickness(4, 5, 4, 5), FontSize = 12, IsEnabled = moveWindow != null,
                ToolTip = "분석기 창을 " + label + " 위치로 이동합니다. 전체 화면에서는 일반 창으로 돌아옵니다." };
            button.Click += (_, _) => positionNote.Text = moveWindow?.Invoke(position) == true
                ? label + " 이동 완료 · 다른 설정은 적용 버튼으로 저장"
                : "창을 이동하지 못했습니다. 다시 눌러 주세요.";
            Grid.SetRow(button, row); Grid.SetColumn(button, column); Grid.SetRowSpan(button, rows); positions.Children.Add(button);
        }
        AddPosition("좌측 위", WindowPosition.TopLeft, 0, 0);
        AddPosition("좌측 하단", WindowPosition.BottomLeft, 1, 0);
        AddPosition("화면 정중앙", WindowPosition.Center, 0, 1, 2);
        AddPosition("오른쪽 위", WindowPosition.TopRight, 0, 2);
        AddPosition("오른쪽 아래", WindowPosition.BottomRight, 1, 2);
        body.Children.Add(positions); body.Children.Add(positionNote);
        body.Children.Add(Label("그래프를 그릴 GPU"));
        var renderGpu = new ComboBox
        {
            Name = "RenderGpuSelector", Tag = "RenderGpuSelector", Margin = new Thickness(0, 4, 0, 6),
            DisplayMemberPath = nameof(GpuAdapterInfo.Name), SelectedValuePath = nameof(GpuAdapterInfo.Id)
        };
        var gpuItems = new List<GpuAdapterInfo> { new("", "자동 선택") };
        string? gpuListError = null;
        try { gpuItems.AddRange(GpuCatalog.Enumerate()); }
        catch { gpuListError = "GPU 목록을 불러오지 못했습니다. 설정을 다시 열어 재시도하세요."; }
        if (!string.IsNullOrEmpty(settings.RenderGpuId) && !gpuItems.Any(g => g.Id == settings.RenderGpuId))
            gpuItems.Add(new(settings.RenderGpuId, "이전에 선택한 GPU · 현재 연결되지 않음"));
        renderGpu.ItemsSource = gpuItems; renderGpu.SelectedValue = settings.RenderGpuId ?? "";
        body.Children.Add(renderGpu);
        body.Children.Add(new TextBlock
        {
            Name = "RendererStatus", Text = string.IsNullOrWhiteSpace(rendererStatus) ? "적용하면 선택한 GPU로 전환합니다." : rendererStatus,
            Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4)
        });
        if (gpuListError != null)
            body.Children.Add(new TextBlock { Text = gpuListError, Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        body.Children.Add(new TextBlock
        {
            Text = "그래프 표시에 적용 · 소리 분석은 CPU 사용\n적용 버튼을 누르면 재시작 없이 전환됩니다.",
            Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), LineHeight = 17
        });
        body.Children.Add(Label("출력 장치"));
        var devices = new ComboBox { Name = "AudioDeviceSelector", Margin = new Thickness(0, 4, 0, 8), DisplayMemberPath = nameof(AudioDeviceInfo.Name), SelectedValuePath = nameof(AudioDeviceInfo.Id) };
        var items = new List<AudioDeviceInfo> { new("", "시스템 기본 출력 장치 · 자동 따라가기") };
        try { items.AddRange(AudioCaptureService.EnumerateDevices()); }
        catch { body.Children.Add(Label("장치 목록을 불러오지 못했습니다. 기본 출력으로 재시도할 수 있습니다.")); }
        if (settings.DeviceId != null && !items.Any(d => d.Id == settings.DeviceId)) items.Add(new(settings.DeviceId, "이전에 선택한 장치 · 현재 연결되지 않음"));
        devices.ItemsSource = items; devices.SelectedValue = settings.DeviceId ?? ""; body.Children.Add(devices);
        var gain = SliderRow(body, "표시 감도", settings.GainDb, -24, 30, " dB", 1);
        var brightness = SliderRow(body, "형광 밝기", settings.Brightness * 100, 15, 100, "%", 1);
        var glow = SliderRow(body, "빛 번짐", settings.Glow * 100, 0, 100, "%", 1);
        var attack = SliderRow(body, "상승 반응", settings.AttackMs, 10, 150, " ms", 5);
        var release = SliderRow(body, "하강 잔상", settings.ReleaseMs, 80, 900, " ms", 10);
        var peak = new CheckBox { Content = "피크 홀드 · 최고점을 잠시 유지", IsChecked = settings.PeakHold };
        var top = new CheckBox { Content = "항상 다른 창 위에 표시", IsChecked = settings.AlwaysOnTop };
        var eco = new CheckBox { Content = "절전 모드 · 목표 30 FPS (기본 목표 60 FPS)", IsChecked = settings.Fps == 30 };
        body.Children.Add(peak); body.Children.Add(top); body.Children.Add(eco);
        body.Children.Add(new TextBlock { Text = "표시 감도는 실제 소리 크기를 바꾸지 않습니다.\n7개 주파수 + FULL RANGE · 13단 이중 형광선.\n오른쪽 0~36은 상대 표시 레벨이며 원기기의 전압 기준과 다릅니다.\n왼쪽 ±12는 원기기의 EQ 눈금을 재현한 장식이며 실제 조절값이 아닙니다.\n모노는 양쪽에 동일하게, 다채널은 전면 좌우만 표시합니다.\nF11 전체 화면 · Ctrl+M 감상 모드 · Esc 복귀\nSH-E70 표시 구조를 바탕으로 여백과 상태 영역을 줄인 화면입니다.", Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 15, 0, 0), LineHeight = 19 });
        save.Click += (_, _) =>
        {
            settings.DeviceId = devices.SelectedValue as string; if (string.IsNullOrEmpty(settings.DeviceId)) settings.DeviceId = null;
            settings.RenderGpuId = renderGpu.SelectedValue as string; if (string.IsNullOrEmpty(settings.RenderGpuId)) settings.RenderGpuId = null;
            settings.GainDb = gain.Value; settings.Brightness = brightness.Value / 100; settings.Glow = glow.Value / 100;
            settings.AttackMs = attack.Value; settings.ReleaseMs = release.Value;
            settings.PeakHold = peak.IsChecked == true; settings.AlwaysOnTop = top.IsChecked == true; settings.Fps = eco.IsChecked == true ? 30 : 60;
            DialogResult = true;
        };
    }
    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 7, 0, 4) };
    private static Slider SliderRow(Panel parent, string label, double value, double min, double max, string unit, double tick)
    {
        var row = new DockPanel { Margin = new Thickness(0, 12, 4, 0) };
        var readout = new TextBlock { Text = Math.Round(value) + unit, HorizontalAlignment = HorizontalAlignment.Right, Foreground = Brushes.LightGray };
        DockPanel.SetDock(readout, Dock.Right); row.Children.Add(readout); row.Children.Add(new TextBlock { Text = label }); parent.Children.Add(row);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = tick, IsSnapToTickEnabled = true, Margin = new Thickness(0, 7, 0, 0) };
        slider.ValueChanged += (_, _) => readout.Text = Math.Round(slider.Value) + unit;
        parent.Children.Add(slider); return slider;
    }
}
