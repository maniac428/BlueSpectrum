using System.Windows;
using System.Windows.Markup;

namespace BlueSpectrum;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0) return Diagnostics.Run(args);
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        ApplyTheme(app);
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show("오류가 발생했습니다. 앱을 다시 실행해 주세요.\n\n" + e.Exception.Message, "Blue Spectrum");
            e.Handled = true; app.Shutdown(1);
        };
        return app.Run(new UI.MainWindow());
    }
    internal static void ApplyTheme(Application app)
    {
        app.Resources = (ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <SolidColorBrush x:Key="{x:Static SystemColors.WindowBrushKey}" Color="#000000"/>
          <SolidColorBrush x:Key="{x:Static SystemColors.ControlBrushKey}" Color="#000000"/>
          <SolidColorBrush x:Key="{x:Static SystemColors.ControlLightBrushKey}" Color="#000000"/>
          <SolidColorBrush x:Key="{x:Static SystemColors.ControlLightLightBrushKey}" Color="#000000"/>
          <SolidColorBrush x:Key="{x:Static SystemColors.ControlDarkBrushKey}" Color="#000000"/>
          <SolidColorBrush x:Key="{x:Static SystemColors.ControlDarkDarkBrushKey}" Color="#000000"/>
          <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}" Color="#000000"/>
          <SolidColorBrush x:Key="{x:Static SystemColors.WindowTextBrushKey}" Color="#CCCCCC"/>
          <SolidColorBrush x:Key="{x:Static SystemColors.ControlTextBrushKey}" Color="#CCCCCC"/>
          <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}" Color="#FFFFFF"/>
          <SolidColorBrush x:Key="{x:Static SystemColors.GrayTextBrushKey}" Color="#666666"/>
          <Style TargetType="Window"><Setter Property="FontFamily" Value="Segoe UI, Malgun Gothic"/><Setter Property="FontSize" Value="13"/><Setter Property="Background" Value="#000000"/><Setter Property="Foreground" Value="#CCCCCC"/></Style>
          <Style TargetType="Button">
            <Setter Property="Foreground" Value="#BBBBBB"/><Setter Property="Background" Value="#000000"/><Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Padding" Value="13,7"/><Setter Property="Cursor" Value="Hand"/><Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border Background="#000000" BorderThickness="0" Padding="{TemplateBinding Padding}"><ContentPresenter RecognizesAccessKey="True" HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter Property="Foreground" Value="#FFFFFF"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter Property="Foreground" Value="#FFFFFF"/></Trigger>
                <Trigger Property="IsPressed" Value="True"><Setter Property="Foreground" Value="#888888"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="#555555"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ContextMenu">
            <Setter Property="Background" Value="#000000"/><Setter Property="Foreground" Value="#BBBBBB"/><Setter Property="BorderThickness" Value="0"/><Setter Property="HasDropShadow" Value="False"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ContextMenu"><Border Background="#000000" Padding="4"><StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/></Border></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="MenuItem">
            <Setter Property="Foreground" Value="#BBBBBB"/><Setter Property="Background" Value="#000000"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Padding" Value="12,7"/><Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem">
              <Border Background="#000000" Padding="{TemplateBinding Padding}"><ContentPresenter ContentSource="Header" RecognizesAccessKey="True"/></Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsHighlighted" Value="True"><Setter Property="Foreground" Value="#FFFFFF"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter Property="Foreground" Value="#FFFFFF"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="#555555"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ToolTip">
            <Setter Property="Background" Value="#000000"/><Setter Property="Foreground" Value="#CCCCCC"/><Setter Property="BorderThickness" Value="0"/><Setter Property="HasDropShadow" Value="False"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ToolTip"><Border Background="#000000" Padding="9,6"><ContentPresenter/></Border></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="Separator"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Separator"><Border Background="#000000" Height="5"/></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="ComboBoxItem">
            <Setter Property="Foreground" Value="#BBBBBB"/><Setter Property="Background" Value="#000000"/><Setter Property="Padding" Value="8,7"/><Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem">
              <Border Background="#000000" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsSelected" Value="True"><Setter Property="Foreground" Value="#EEEEEE"/></Trigger>
                <Trigger Property="IsHighlighted" Value="True"><Setter Property="Foreground" Value="#FFFFFF"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="#555555"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ComboBox">
            <Setter Property="Foreground" Value="#CCCCCC"/><Setter Property="Background" Value="#000000"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Padding" Value="8,5"/><Setter Property="MinHeight" Value="32"/><Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
              <Grid Background="#000000">
                <ToggleButton Focusable="False" ClickMode="Press" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                  <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="#000000"><Path Data="M 0 0 L 4 4 L 8 0" Stroke="#999999" StrokeThickness="1.2" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,9,0"/></Border></ControlTemplate></ToggleButton.Template>
                </ToggleButton>
                <ContentPresenter Margin="8,5,28,5" VerticalAlignment="Center" IsHitTestVisible="False" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"/>
                <Popup x:Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False" PopupAnimation="None">
                  <Border Background="#000000" MinWidth="{TemplateBinding ActualWidth}" MaxHeight="{TemplateBinding MaxDropDownHeight}"><ScrollViewer Background="#000000" BorderThickness="0" CanContentScroll="True" VerticalScrollBarVisibility="Auto"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer></Border>
                </Popup>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter Property="Foreground" Value="#FFFFFF"/></Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter Property="Foreground" Value="#FFFFFF"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="#555555"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="CheckBox">
            <Setter Property="Foreground" Value="#BBBBBB"/><Setter Property="Background" Value="#000000"/><Setter Property="Margin" Value="0,7"/><Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox">
              <Grid Background="#000000"><Grid.ColumnDefinitions><ColumnDefinition Width="22"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                <Border Width="12" Height="12" HorizontalAlignment="Left" VerticalAlignment="Center" Background="#000000" BorderBrush="#666666" BorderThickness="1"><Path x:Name="mark" Data="M 1 5 L 4 8 L 10 1" Stroke="#EEEEEE" StrokeThickness="1.4" Visibility="Collapsed"/></Border>
                <ContentPresenter Grid.Column="1" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsChecked" Value="True"><Setter TargetName="mark" Property="Visibility" Value="Visible"/></Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter Property="Foreground" Value="#FFFFFF"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter Property="Foreground" Value="#FFFFFF"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="InvisibleTrackButton" TargetType="RepeatButton">
            <Setter Property="Focusable" Value="False"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ScrollViewer"><Setter Property="Background" Value="#000000"/><Setter Property="BorderThickness" Value="0"/></Style>
          <Style x:Key="ScrollLineButton" TargetType="RepeatButton">
            <Setter Property="Focusable" Value="False"/><Setter Property="Width" Value="10"/><Setter Property="Height" Value="10"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="RepeatButton"><Border Background="#000000"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ScrollBar">
            <Setter Property="Background" Value="#000000"/><Setter Property="Foreground" Value="#777777"/><Setter Property="Width" Value="10"/><Setter Property="MinWidth" Value="0"/><Setter Property="MinHeight" Value="0"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
              <DockPanel Background="#000000">
                <RepeatButton x:Name="lineUp" DockPanel.Dock="Top" Style="{StaticResource ScrollLineButton}" Command="ScrollBar.LineUpCommand"><Path x:Name="upArrow" Data="M 0 4 L 3 1 L 6 4" Stroke="{TemplateBinding Foreground}" StrokeThickness="1"/></RepeatButton>
                <RepeatButton x:Name="lineDown" DockPanel.Dock="Bottom" Style="{StaticResource ScrollLineButton}" Command="ScrollBar.LineDownCommand"><Path x:Name="downArrow" Data="M 0 1 L 3 4 L 6 1" Stroke="{TemplateBinding Foreground}" StrokeThickness="1"/></RepeatButton>
                <Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" ViewportSize="{TemplateBinding ViewportSize}" Orientation="{TemplateBinding Orientation}" IsDirectionReversed="True">
                  <Track.DecreaseRepeatButton><RepeatButton x:Name="decrease" Style="{StaticResource InvisibleTrackButton}" Command="ScrollBar.PageUpCommand"/></Track.DecreaseRepeatButton>
                  <Track.Thumb><Thumb x:Name="thumb" Width="6" MinHeight="18" Background="{TemplateBinding Foreground}"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{TemplateBinding Background}"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                  <Track.IncreaseRepeatButton><RepeatButton x:Name="increase" Style="{StaticResource InvisibleTrackButton}" Command="ScrollBar.PageDownCommand"/></Track.IncreaseRepeatButton>
                </Track>
              </DockPanel>
              <ControlTemplate.Triggers>
                <Trigger Property="Orientation" Value="Horizontal">
                  <Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="False"/><Setter TargetName="decrease" Property="Command" Value="ScrollBar.PageLeftCommand"/><Setter TargetName="increase" Property="Command" Value="ScrollBar.PageRightCommand"/>
                  <Setter TargetName="lineUp" Property="DockPanel.Dock" Value="Left"/><Setter TargetName="lineDown" Property="DockPanel.Dock" Value="Right"/><Setter TargetName="lineUp" Property="Command" Value="ScrollBar.LineLeftCommand"/><Setter TargetName="lineDown" Property="Command" Value="ScrollBar.LineRightCommand"/>
                  <Setter TargetName="upArrow" Property="Data" Value="M 4 0 L 1 3 L 4 6"/><Setter TargetName="downArrow" Property="Data" Value="M 1 0 L 4 3 L 1 6"/>
                  <Setter TargetName="thumb" Property="Width" Value="Auto"/><Setter TargetName="thumb" Property="Height" Value="6"/><Setter TargetName="thumb" Property="MinHeight" Value="0"/><Setter TargetName="thumb" Property="MinWidth" Value="18"/>
                </Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter Property="Foreground" Value="#AAAAAA"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
            <Style.Triggers><Trigger Property="Orientation" Value="Horizontal"><Setter Property="Width" Value="Auto"/><Setter Property="Height" Value="10"/></Trigger></Style.Triggers>
          </Style>
          <Style TargetType="Slider">
            <Setter Property="Background" Value="#000000"/><Setter Property="Foreground" Value="#BBBBBB"/><Setter Property="MinHeight" Value="20"/><Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider">
              <Grid Background="#000000" Height="20">
                <Border Height="1" Background="#555555" VerticalAlignment="Center" Margin="4,0"/>
                <Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" Orientation="{TemplateBinding Orientation}" IsDirectionReversed="{TemplateBinding IsDirectionReversed}">
                  <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource InvisibleTrackButton}" Command="Slider.DecreaseLarge"/></Track.DecreaseRepeatButton>
                  <Track.Thumb><Thumb Width="8" Height="14" Background="{TemplateBinding Foreground}"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{TemplateBinding Background}"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                  <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource InvisibleTrackButton}" Command="Slider.IncreaseLarge"/></Track.IncreaseRepeatButton>
                </Track>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter Property="Foreground" Value="#FFFFFF"/></Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter Property="Foreground" Value="#FFFFFF"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
        </ResourceDictionary>
        """);
    }
}
