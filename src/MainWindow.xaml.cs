using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
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
    System.Windows.Forms.ToolStripMenuItem miStatus, miToggle, miOpen, miLang, miExit;
    System.Drawing.Icon iconGreen, iconGray;
    DispatcherTimer anim, guard, ticker;
    bool stateOn = false;
    bool logExpanded = false;
    DateTime? connectedAt = null;
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
        LogBtn.Click += (s, e) => ToggleLog();
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
        ApplyLang();
        ApplyLogState();
        UpdateStatus();

        if (!string.IsNullOrWhiteSpace(config.updateRepo))
            _ = CheckUpdatesAsync(config.updateRepo);
    }

    void ApplyLogState()
    {
        if (logExpanded)
        {
            LogCard.Visibility = Visibility.Visible;
            LogSplitter.Visibility = Visibility.Visible;
            SpacerRow.Height = new GridLength(0.2, GridUnitType.Star);
            LogRow.Height = new GridLength(1, GridUnitType.Star);
            LogRow.MinHeight = 60;
            Height = 660;
        }
        else
        {
            LogCard.Visibility = Visibility.Collapsed;
            LogSplitter.Visibility = Visibility.Collapsed;
            SpacerRow.Height = new GridLength(0);
            LogRow.Height = new GridLength(0);
            LogRow.MinHeight = 0;
            Height = 460;
        }
        LogBtn.Content = Lang.T("log") + (logExpanded ? "  ▴" : "  ▾");
    }

    void ToggleLog()
    {
        logExpanded = !logExpanded;
        ApplyLogState();
    }

    void ApplyLang()
    {
        SubText.Text = Lang.T("subtitle");
        HintText.Text = Lang.T("hint");
        LogCaption.Text = Lang.T("log");
        HappBtn.Content = Lang.T("open_vpn");
        IpBtn.Content = Lang.T("check_ip");
        LogBtn.Content = Lang.T("log") + (logExpanded ? "  ▴" : "  ▾");
        SettingsBtn.ToolTip = Lang.T("settings");
        MinBtn.ToolTip = Lang.T("minimize");
        CloseBtn.ToolTip = Lang.T("close");
        if (tray != null)
        {
            miOpen.Text = Lang.T("open_app");
            miExit.Text = Lang.T("exit");
            miLang.Checked = Lang.English;
        }
    }

    async System.Threading.Tasks.Task CheckUpdatesAsync(string repo)
    {
        var ver = typeof(App).Assembly.GetName().Version.ToString(3);
        var res = await UpdateChecker.CheckAsync(repo, ver);
        if (res.version != null)
        {
            Dispatcher.Invoke(() =>
            {
                Log(Lang.T("update_available") + ": " + res.version);
                Ui.ShowToast("Pconnect", Lang.T("update_available") + ": " + res.version, "#6D5CF7");
            });
        }
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

        miStatus = new System.Windows.Forms.ToolStripMenuItem(Lang.T("disconnected")) { Enabled = false };
        miToggle = new System.Windows.Forms.ToolStripMenuItem(Lang.T("connect"));
        miOpen = new System.Windows.Forms.ToolStripMenuItem(Lang.T("open_app"));
        miLang = new System.Windows.Forms.ToolStripMenuItem("English") { Checked = Lang.English };
        miExit = new System.Windows.Forms.ToolStripMenuItem(Lang.T("exit"));

        miToggle.Click += (s, e) => { if (TunnelUp()) Disconnect(); else Connect(); };
        miOpen.Click += (s, e) => { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); };
        miLang.Click += (s, e) => { Prefs.Set("Lang", !Lang.English); ApplyLang(); UpdateStatus(); };
        miExit.Click += (s, e) => { tray.Visible = false; tray.Dispose(); Close(); };
        tray.DoubleClick += (s, e) => { Show(); Activate(); };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(miStatus);
        menu.Items.Add(miToggle);
        menu.Items.Add(miOpen);
        menu.Items.Add(miLang);
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

        ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        ticker.Tick += (s, e) => UpdateTimer();
        ticker.Start();
    }

    void UpdateTimer()
    {
        if (connectedAt != null && stateOn)
        {
            var ts = DateTime.Now - connectedAt.Value;
            TimerText.Text = ((int)ts.TotalHours).ToString("D2") + ":" + ts.Minutes.ToString("D2") + ":" + ts.Seconds.ToString("D2");
        }
        else if (TimerText.Text.Length > 0)
        {
            TimerText.Text = "";
        }
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
                IpText.Text = Lang.T("your_ip") + ": " + ip;
                try
                {
                    var info = hc.GetStringAsync("https://ipinfo.io/" + ip + "/json").GetAwaiter().GetResult();
                    var node = JsonNode.Parse(info);
                    var cc = (string)node?["country"];
                    if (!string.IsNullOrEmpty(cc))
                    {
                        var city = (string)node?["city"];
                        CountryText.Text = Flag(cc) + " " + (string.IsNullOrEmpty(city) ? cc : city + ", " + cc);
                    }
                }
                catch { CountryText.Text = ""; }
            }
        }
        catch { IpText.Text = Lang.T("your_ip") + ": —"; CountryText.Text = ""; }
    }

    static string Flag(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != 2) return "";
        return char.ConvertFromUtf32(0x1F1E6 + (char.ToUpper(code[0]) - 'A'))
             + char.ConvertFromUtf32(0x1F1E6 + (char.ToUpper(code[1]) - 'A'));
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
            StatusText.Text = Lang.T("connected");
            StatusText.Foreground = Green;
            PowerBtn.Content = Lang.T("disconnect");
            PowerBtn.Background = Red;
        }
        else
        {
            StatusText.Text = Lang.T("disconnected");
            StatusText.Foreground = Dark;
            PowerBtn.Content = Lang.T("connect");
            PowerBtn.Background = Green;
        }
        Prog.Visibility = Visibility.Collapsed;
        if (tray != null)
        {
            tray.Icon = on ? iconGreen : iconGray;
            tray.Text = on ? Lang.T("tray_on") : Lang.T("tray_off");
            miStatus.Text = on ? Lang.T("connected") : Lang.T("disconnected");
            miToggle.Text = on ? Lang.T("disconnect") : Lang.T("connect");
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
        connectedAt = DateTime.Now;
        Ui.PlaySound(true);
        Ui.ShowToast("Pconnect", Lang.T("connected_toast") + " — " + IpText.Text.Replace(Lang.T("your_ip") + ": ", ""), "#16A34A");
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
        IpText.Text = Lang.T("your_ip") + ": —";
        CountryText.Text = "";
        TimerText.Text = "";
        connectedAt = null;
        UpdateStatus();
        Ui.PlaySound(false);
        Ui.ShowToast("Pconnect", Lang.T("disconnected_toast"), "#9CA3AF");
        Log("Готово.");
    }
}
