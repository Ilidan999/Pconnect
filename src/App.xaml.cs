using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Pconnect;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var splash = BuildSplash();
        splash.Show();
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        t.Tick += (s, ev) => { t.Stop(); new MainWindow().Show(); splash.Close(); };
        t.Start();
    }

    Window BuildSplash()
    {
        var logo = new Image { Width = 66, Height = 66, HorizontalAlignment = HorizontalAlignment.Center };
        var png = Path.Combine(ConfigStore.AppDir, "assets", "Pconnect.png");
        if (File.Exists(png))
        {
            try { logo.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(png)); }
            catch { }
        }

        var title = new TextBlock
        {
            Text = "Pconnect", FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = Ui.MakeBrush("#1B2430"), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var welcome = new TextBlock
        {
            Text = "Welcome", FontSize = 15, Foreground = Ui.MakeBrush("#6D5CF7"),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0)
        };
        var prog = new ProgressBar
        {
            Width = 150, Height = 4, IsIndeterminate = true,
            Margin = new Thickness(0, 18, 0, 0), Foreground = Ui.MakeBrush("#6D5CF7")
        };

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(logo);
        stack.Children.Add(title);
        stack.Children.Add(welcome);
        stack.Children.Add(prog);

        var border = new Border { CornerRadius = new CornerRadius(22), Margin = new Thickness(10), Child = stack };
        border.Background = new LinearGradientBrush(
            Ui.MakeBrush("#F6F7FC").Color, Ui.MakeBrush("#E4E7F4").Color, new Point(0, 0), new Point(0, 1));
        border.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.20, Color = Colors.Black };

        return new Window
        {
            Width = 330, Height = 230, WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Background = Brushes.Transparent, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize, Content = border
        };
    }
}
