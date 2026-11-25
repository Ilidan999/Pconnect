using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Pconnect;

static class Prefs
{
    const string Key = @"HKEY_CURRENT_USER\Software\Pconnect";
    public static bool Get(string name) => (int?)Microsoft.Win32.Registry.GetValue(Key, name, 0) == 1;
    public static void Set(string name, bool value) => Microsoft.Win32.Registry.SetValue(Key, name, value ? 1 : 0);
    public static string GetStr(string name, string def = "") =>
        (string)Microsoft.Win32.Registry.GetValue(Key, name, def) ?? def;
    public static void SetStr(string name, string value) =>
        Microsoft.Win32.Registry.SetValue(Key, name, value);
}

static class Ui
{
    public static SolidColorBrush MakeBrush(string hex) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    // ---------- звуки (генерируются локально) ----------
    public static void PlaySound(bool connected)
    {
        try
        {
            var freqs = connected ? new double[] { 659.25, 783.99, 1046.5 } : new double[] { 783.99, 523.25 };
            var bytes = BuildWav(freqs, connected ? 0.14 : 0.16, connected ? 0.35 : 0.30);
            using (var ms = new MemoryStream(bytes))
            using (var sp = new System.Media.SoundPlayer(ms))
                sp.Play();
        }
        catch { }
    }

    static byte[] BuildWav(double[] freqs, double dur, double vol)
    {
        int rate = 32000, nPer = (int)(rate * dur), gap = (int)(rate * 0.015);
        int total = (nPer + gap) * freqs.Length;
        int dataLen = total * 2;
        var buf = new byte[44 + dataLen];
        WriteAscii(buf, 0, "RIFF"); WriteInt(buf, 4, 36 + dataLen); WriteAscii(buf, 8, "WAVE");
        WriteAscii(buf, 12, "fmt "); WriteInt(buf, 16, 16); WriteShort(buf, 20, 1); WriteShort(buf, 22, 1);
        WriteInt(buf, 24, rate); WriteInt(buf, 28, rate * 2); WriteShort(buf, 32, 2); WriteShort(buf, 34, 16);
        WriteAscii(buf, 36, "data"); WriteInt(buf, 40, dataLen);
        int idx = 44;
        foreach (var f in freqs)
        {
            for (int j = 0; j < nPer; j++)
            {
                double t = (double)j / rate;
                double a = Math.Min(1.0, t / 0.004);
                double d = Math.Exp(-3.0 * t / dur);
                double v = Math.Sin(2 * Math.PI * f * t) * a * d * vol;
                short s = (short)Math.Max(-32767, Math.Min(32767, v * 32767));
                WriteShort(buf, idx, s); idx += 2;
            }
            idx += gap * 2;
        }
        return buf;
    }

    static void WriteAscii(byte[] b, int o, string s) { for (int i = 0; i < s.Length; i++) b[o + i] = (byte)s[i]; }
    static void WriteInt(byte[] b, int o, int v) { BitConverter.GetBytes(v).CopyTo(b, o); }
    static void WriteShort(byte[] b, int o, short v) { BitConverter.GetBytes(v).CopyTo(b, o); }

    // ---------- всплывающее уведомление (само закрывается) ----------
    public static void ShowToast(string title, string message, string dotHex)
    {
        try
        {
            var dot = new Ellipse { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center, Fill = MakeBrush(dotHex) };
            var titleTb = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = MakeBrush("#F4F5F9") };
            var msgTb = new TextBlock { Text = message, FontSize = 11, Foreground = MakeBrush("#A5ABC0"), TextWrapping = TextWrapping.Wrap, MaxWidth = 250 };
            var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            right.Children.Add(titleTb); right.Children.Add(msgTb);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 12, 16, 12) };
            row.Children.Add(dot); row.Children.Add(right);
            var border = new Border { CornerRadius = new CornerRadius(14), Margin = new Thickness(6), Background = MakeBrush("#262A3A"), BorderBrush = MakeBrush("#3A3F54"), BorderThickness = new Thickness(1), Child = row };
            var w = new Window
            {
                Width = 330, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None,
                AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true,
                ShowInTaskbar = false, ShowActivated = false, ResizeMode = ResizeMode.NoResize, Content = border
            };
            w.Loaded += (s, e) => { var wa = SystemParameters.WorkArea; w.Left = wa.Right - w.ActualWidth - 12; w.Top = wa.Bottom - w.ActualHeight - 12; };
            w.Show();
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (s, e) => { t.Stop(); w.Close(); };
            t.Start();
        }
        catch { }
    }

    // ---------- логотип (встроенный ресурс, с запасным файлом) ----------
    public static System.Windows.Media.Imaging.BitmapImage LoadLogo()
    {
        try
        {
            var asm = typeof(App).Assembly;
            var stream = asm.GetManifestResourceStream("Pconnect.Assets.Pconnect.png");
            if (stream == null)
            {
                var png = System.IO.Path.Combine(ConfigStore.AppDir, "assets", "Pconnect.png");
                if (File.Exists(png))
                    return new System.Windows.Media.Imaging.BitmapImage(new Uri(png));
                return null;
            }
            using (stream)
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.StreamSource = stream;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
        }
        catch { return null; }
    }

    // ---------- короткий собственный звук уведомления ----------
    public static void PlayNotify()
    {
        try
        {
            var bytes = BuildWav(new double[] { 880.0, 1174.66 }, 0.12, 0.22);
            using (var ms = new MemoryStream(bytes))
            using (var sp = new System.Media.SoundPlayer(ms))
                sp.Play();
        }
        catch { }
    }

    static ControlTemplate MakeButtonTemplate(Color bg)
    {
        var b = new FrameworkElementFactory(typeof(Border));
        b.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        b.SetValue(Border.BackgroundProperty, new SolidColorBrush(bg));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        b.AppendChild(cp);
        return new ControlTemplate(typeof(Button)) { VisualTree = b };
    }

    // ---------- своё модальное сообщение (вместо MessageBox + Windows-звука) ----------
    public static void ShowMessage(Window owner, string message, string title = "Pconnect", bool warning = false)
    {
        PlayNotify();
        try
        {
            var titleTb = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = MakeBrush("#F4F5F9") };
            var msgTb = new TextBlock { Text = message, FontSize = 12.5, Foreground = MakeBrush("#A5ABC0"), TextWrapping = TextWrapping.Wrap, MaxWidth = 330, Margin = new Thickness(0, 8, 0, 0) };
            var okBtn = new Button
            {
                Content = "OK",
                Width = 92, Height = 34,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Template = MakeButtonTemplate(warning ? Color.FromRgb(0xEF, 0x44, 0x44) : Color.FromRgb(0x22, 0xC5, 0x5E))
            };
            var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            stack.Children.Add(titleTb);
            stack.Children.Add(msgTb);
            stack.Children.Add(okBtn);
            var border = new Border { CornerRadius = new CornerRadius(14), Background = MakeBrush("#1E2029"), BorderBrush = MakeBrush("#2E3245"), BorderThickness = new Thickness(1), Child = stack };
            var w = new Window
            {
                Width = 380, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None,
                AllowsTransparency = true, Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, Content = border
            };
            if (owner != null) w.Owner = owner;
            okBtn.Click += (s, e) => w.Close();
            w.ShowDialog();
        }
        catch { }
    }
}
