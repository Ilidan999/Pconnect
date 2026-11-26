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
    static System.Threading.Mutex instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // разрешаем только один экземпляр приложения
        instanceMutex = new System.Threading.Mutex(true, "Pconnect_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        bool isAutostart = e.Args.Contains("--autostart");
        var splash = BuildSplash();
        splash.Show();
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        t.Tick += (s, ev) =>
        {
            t.Stop();
            var main = new MainWindow();
            main.Show();
            if (Prefs.Get("StartInTray") && isAutostart) main.Hide();
            splash.Close();
        };
        t.Start();
    }

    Window BuildSplash()
    {
        var logo = new Image { Width = 66, Height = 66, HorizontalAlignment = HorizontalAlignment.Center };
        var logoImg = Ui.LoadLogo();
        if (logoImg != null) logo.Source = logoImg;

        var title = new TextBlock
        {
            Text = "Pconnect", FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = Ui.MakeBrush("#F4F5F9"), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var welcome = new TextBlock
        {
            Text = "Welcome", FontSize = 15, Foreground = Ui.MakeBrush("#8B5CF6"),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0)
        };
        var prog = new ProgressBar
        {
            Width = 150, Height = 4, IsIndeterminate = true,
            Margin = new Thickness(0, 18, 0, 0), Foreground = Ui.MakeBrush("#8B5CF6")
        };

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(logo);
        stack.Children.Add(title);
        stack.Children.Add(welcome);
        stack.Children.Add(prog);

        var border = new Border { CornerRadius = new CornerRadius(22), Margin = new Thickness(10), Child = stack };
        border.Background = new LinearGradientBrush(
            Ui.MakeBrush("#1B1D29").Color, Ui.MakeBrush("#10121B").Color, new Point(0, 0), new Point(0, 1));
        border.BorderBrush = Ui.MakeBrush("#2A2D3D");
        border.BorderThickness = new Thickness(1);

        return new Window
        {
            Width = 330, Height = 230, WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Background = Brushes.Transparent, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize, Content = border
        };
    }
}
