using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pconnect;

public partial class MainWindow : Window
{
    AppConfig config;
    Profile current;
    System.Windows.Forms.NotifyIcon tray;
    System.Windows.Forms.ToolStripMenuItem miStatus, miToggle;
    System.Drawing.Icon iconGreen, iconGray;
    DispatcherTimer anim, guard;
    bool stateOn = false;
    int phase = 0;

    static readonly string[] DefaultKeywords = {
        "zapret","winws","goodbyedpi","spoofdpi","byedpi","nfqws","tpws",
        "dpi_bypass","dpi-bypass","ciadpi","zaborona","anticenz","greentunnel"
    };

    static readonly SolidColorBrush Green = MakeBrush("#16A34A");
    static readonly SolidColorBrush GreenLight = MakeBrush("#5EE08A");
    static readonly SolidColorBrush Red = MakeBrush("#DC2626");
    static readonly SolidColorBrush Gray = MakeBrush("#9CA3AF");
    static readonly SolidColorBrush GrayLight = MakeBrush("#D7DBE2");
    static readonly SolidColorBrush Dark = MakeBrush("#1B2430");

    static SolidColorBrush MakeBrush(string hex) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public MainWindow()
    {
        InitializeComponent();

        PowerBtn.Click += (s, e) => { PowerBtn.IsEnabled = false; if (TunnelUp()) Disconnect(); else Connect(); PowerBtn.IsEnabled = true; };
        HappBtn.Click += (s, e) => OpenClient();
        IpBtn.Click += (s, e) => { UpdateIp(); Log("Текущий IP: " + IpText.Text); };
        SettingsBtn.Click += (s, e) => OpenSettings();
        MinBtn.Click += (s, e) => Hide();
        CloseBtn.Click += (s, e) => Hide();
        PreviewMouseLeftButtonDown += OnDrag;
        Loaded += OnLoaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        config = ConfigStore.Load();
        current = config.profiles.FirstOrDefault(p => p.name == config.defaultProfile)
               ?? config.profiles.FirstOrDefault();

        if (current == null)
        {
            Log("Не найден config.json или в нём нет профилей.");
            PowerBtn.IsEnabled = false;
        }
        else
        {
            Log("Pconnect готов. Профиль: " + current.name);
        }

        var png = Path.Combine(ConfigStore.AppDir, "assets", "Pconnect.png");
        if (File.Exists(png))
        {
            try { Logo.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(png)); }
            catch { }
        }

        SetupTray();
        SetupAnim();
        SetupGuard();
        RegisterGlobalHotkey();
        UpdateStatus();
    }

    // ---------- перетаскивание ----------
    void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (HasInteractiveAncestor(e.OriginalSource)) return;
        try
        {
            var h = new WindowInteropHelper(this).Handle;
            ReleaseCapture();
            SendMessage(h, 0xA1, (IntPtr)2, IntPtr.Zero);
        }
        catch { }
    }

    bool HasInteractiveAncestor(object el)
    {
        var cur = el as DependencyObject;
        int n = 0;
        while (cur != null && n < 30)
        {
            var t = cur.GetType().Name;
            if (t == "Button" || t == "TextBox" || t == "ComboBox" || t == "CheckBox" ||
                t == "ScrollBar" || t == "GridSplitter" || t == "Thumb" || t == "RepeatButton")
                return true;
            if (cur is Visual) cur = VisualTreeHelper.GetParent(cur);
            else if (cur is ContentElement) cur = System.Windows.ContentOperations.GetParent((ContentElement)cur);
            else cur = null;
            n++;
        }
        return false;
    }

    // ---------- трей ----------
    void SetupTray()
    {
        iconGreen = MakeIcon("#16A34A");
        iconGray = MakeIcon("#9CA3AF");

        tray = new System.Windows.Forms.NotifyIcon { Icon = iconGray, Text = "Pconnect", Visible = true };

        miStatus = new System.Windows.Forms.ToolStripMenuItem("Не подключено") { Enabled = false };
        miToggle = new System.Windows.Forms.ToolStripMenuItem("Включить VPN");
        var miOpen = new System.Windows.Forms.ToolStripMenuItem("Открыть Pconnect");
        var miExit = new System.Windows.Forms.ToolStripMenuItem("Выход");

        miToggle.Click += (s, e) => { if (TunnelUp()) Disconnect(); else Connect(); };
        miOpen.Click += (s, e) => { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); };
        miExit.Click += (s, e) => { tray.Visible = false; tray.Dispose(); Close(); };
        tray.DoubleClick += (s, e) => { Show(); Activate(); };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(miStatus);
        menu.Items.Add(miToggle);
        menu.Items.Add(miOpen);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(miExit);
        tray.ContextMenuStrip = menu;
    }

    System.Drawing.Icon MakeIcon(string hex)
    {
        var bmp = new System.Drawing.Bitmap(16, 16);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var br = new System.Drawing.SolidBrush(System.Drawing.ColorTranslator.FromHtml(hex)))
                g.FillEllipse(br, 1, 1, 14, 14);
        }
        var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(bmp.GetHicon()).Clone();
        bmp.Dispose();
        return icon;
    }

    void SetupAnim()
    {
        anim = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        anim.Tick += (s, e) =>
        {
            phase++;
            if (stateOn) Dot.Fill = (phase % 2 == 0) ? GreenLight : Green;
            else Dot.Fill = (phase % 2 == 0) ? GrayLight : Gray;
        };
        anim.Start();
    }

    void SetupGuard()
    {
        guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        guard.Tick += (s, e) =>
        {
            if (Prefs.Get("KillSwitch") && stateOn && current != null && !TunnelUp())
            {
                Log("Обнаружен обрыв VPN — переподключаю…");
                stateOn = false;
                Ui.ShowToast("Pconnect", "VPN разорван — переподключение…", "#DC2626");
                Dispatcher.BeginInvoke(new Action(Connect));
            }
        };
        guard.Start();
    }

    void RegisterGlobalHotkey()
    {
        try
        {
            var h = new WindowInteropHelper(this).Handle;
            RegisterHotKey(h, 1, 0x0006, 0x56); // Ctrl+Shift+V
            var src = HwndSource.FromHwnd(h);
            src.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == 0x0312 && wParam.ToInt32() == 1)
                {
                    Dispatcher.BeginInvoke(new Action(() => { if (TunnelUp()) Disconnect(); else Connect(); }));
                }
                return IntPtr.Zero;
            });
            Log("Горячая клавиша Ctrl+Shift+V включена.");
        }
        catch { Log("Не удалось зарегистрировать горячую клавишу."); }
    }

    // ---------- утилиты ----------
    void DoEvents() => Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

    void Log(string msg)
    {
        LogBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + msg + "\r\n");
        LogBox.ScrollToEnd();
        DoEvents();
    }

    bool TunnelUp()
    {
        if (current == null || string.IsNullOrEmpty(current.adapter)) return false;
        return NetworkInterface.GetAllNetworkInterfaces()
            .Any(n => n.Name.Equals(current.adapter, StringComparison.OrdinalIgnoreCase)
                   && n.OperationalStatus == OperationalStatus.Up);
    }

    void RunAction(string cmd)
    {
        if (string.IsNullOrEmpty(cmd)) return;
        try
        {
            if (cmd.Contains("://"))
                Process.Start(new ProcessStartInfo(cmd) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("cmd.exe", "/c " + cmd) { CreateNoWindow = true, UseShellExecute = false });
        }
        catch (Exception ex) { Log("Ошибка: " + ex.Message); }
    }

    void RunPowerShell(string script)
    {
        try
        {
            Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"" + script + "\"")
            { CreateNoWindow = true, UseShellExecute = false });
        }
        catch { }
    }

    void StopInterfering()
    {
        var script = "";
        foreach (var s in current.interferingServices)
            script += "Stop-Service -Name '" + s + "' -Force -ErrorAction SilentlyContinue;Set-Service -Name '" + s + "' -StartupType Disabled -ErrorAction SilentlyContinue;";
        foreach (var p in current.interferingProcesses)
            script += "Stop-Process -Name '" + p + "' -Force -ErrorAction SilentlyContinue;";

        var kw = current.interferingKeywords
            .Concat(DefaultKeywords)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();
        if (kw.Count > 0)
        {
            var re = "(?i)(" + string.Join("|", kw.Select(Regex.Escape)) + ")";
            script += "Get-Service | Where-Object { $_.Name -match '" + re + "' -or $_.DisplayName -match '" + re + "' } | ForEach-Object { Stop-Service -Name $_.Name -Force -ErrorAction SilentlyContinue; Set-Service -Name $_.Name -StartupType Disabled -ErrorAction SilentlyContinue };";
            script += "Get-Process | Where-Object { $_.ProcessName -match '" + re + "' } | Stop-Process -Force -ErrorAction SilentlyContinue;";
        }

        if (string.IsNullOrEmpty(script)) { Log("Нет мешающих программ."); return; }
        RunPowerShell(script);
        Log("Мешающие программы отключены.");
    }

    void RestoreInterfering()
    {
        if (current.interferingServices.Count == 0) return;
        var script = "";
        foreach (var s in current.interferingServices)
            script += "Set-Service -Name '" + s + "' -StartupType Automatic -ErrorAction SilentlyContinue;Start-Service -Name '" + s + "' -ErrorAction SilentlyContinue;";
        RunPowerShell(script);
        Log("Мешающие программы возвращены.");
    }

    void FixRouting()
    {
        try
        {
            RunPowerShell("Set-NetIPInterface -InterfaceAlias '" + current.adapter + "' -AddressFamily IPv4 -IgnoreDefaultRoutes Disabled");
            Log("Маршрутизация включена.");
        }
        catch (Exception ex) { Log("Не удалось настроить маршрут: " + ex.Message); }
    }

    void UpdateIp()
    {
        try
        {
            using (var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                var ip = hc.GetStringAsync("https://api.ipify.org").GetAwaiter().GetResult().Trim();
                IpText.Text = "Ваш IP: " + ip;
            }
        }
        catch { IpText.Text = "Ваш IP: не определён"; }
    }

    void OpenClient()
    {
        if (!string.IsNullOrEmpty(current?.clientPath) && File.Exists(current.clientPath))
            Process.Start(new ProcessStartInfo(current.clientPath) { UseShellExecute = true });
        else
            Log("Не указан clientPath в профиле.");
    }

    void OpenSettings()
    {
        var sw = new SettingsWindow { Owner = this };
        sw.ShowDialog();
        config = ConfigStore.Load();
        current = config.profiles.FirstOrDefault(p => p.name == config.defaultProfile)
               ?? config.profiles.FirstOrDefault();
        UpdateStatus();
    }

    // ---------- статус ----------
    void SetWorking(bool working)
    {
        Prog.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateStatus()
    {
        bool on = TunnelUp();
        stateOn = on;
        if (on)
        {
            StatusText.Text = "Подключено";
            StatusText.Foreground = Green;
            PowerBtn.Content = "Выключить VPN";
            PowerBtn.Background = Red;
        }
        else
        {
            StatusText.Text = "Не подключено";
            StatusText.Foreground = Dark;
            PowerBtn.Content = "Включить VPN";
            PowerBtn.Background = Green;
        }
        Prog.Visibility = Visibility.Collapsed;
        if (tray != null)
        {
            tray.Icon = on ? iconGreen : iconGray;
            tray.Text = on ? "Pconnect — подключено" : "Pconnect — не подключено";
            miStatus.Text = on ? "Подключено" : "Не подключено";
            miToggle.Text = on ? "Выключить VPN" : "Включить VPN";
        }
        DoEvents();
    }

    // ---------- подключение ----------
    void Connect()
    {
        if (current == null) return;
        Log("================ ЗАПУСК VPN ================");
        SetWorking(true);
        StopInterfering();
        Log("Команда подключения: " + current.connect);
        RunAction(current.connect);

        if (!string.IsNullOrEmpty(current.adapter))
        {
            for (int i = 0; i < 50; i++)
            {
                if (TunnelUp()) break;
                Thread.Sleep(750);
                DoEvents();
            }
            if (!TunnelUp())
            {
                Log("Не удалось подключиться — проверьте настройки профиля.");
                UpdateStatus();
                return;
            }
            Log("Туннель «" + current.adapter + "» поднят.");
            if (current.fixRouting) FixRouting();
        }
        else
        {
            Log("В профиле не указан адаптер — статус не отслеживается.");
            Thread.Sleep(3000);
        }

        UpdateStatus();
        UpdateIp();
        Ui.PlaySound(true);
        Ui.ShowToast("Pconnect", "VPN подключён — " + IpText.Text.Replace("Ваш IP: ", ""), "#16A34A");
        Log("Готово.");
    }

    void Disconnect()
    {
        if (current == null) return;
        Log("================ ОСТАНОВКА VPN ================");
        if (TunnelUp())
        {
            SetWorking(true);
            Log("Команда отключения: " + current.disconnect);
            RunAction(current.disconnect);
            for (int i = 0; i < 25; i++)
            {
                if (!TunnelUp()) break;
                Thread.Sleep(500);
                DoEvents();
            }
        }
        else
        {
            Log("VPN уже не подключён.");
        }
        RestoreInterfering();
        IpText.Text = "Ваш IP: —";
        UpdateStatus();
        Ui.PlaySound(false);
        Ui.ShowToast("Pconnect", "VPN выключен", "#9CA3AF");
        Log("Готово.");
    }
}
