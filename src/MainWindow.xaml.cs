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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Pconnect;

public partial class MainWindow : Window
{
    AppConfig config;
    Profile current;
    System.Windows.Forms.NotifyIcon tray;
    System.Windows.Forms.ToolStripMenuItem miStatus, miToggle, miOpen, miLang, miExit;
    System.Drawing.Icon iconGreen, iconGray;
    DispatcherTimer guard, ticker;
    bool stateOn = false;
    bool logExpanded = false;
    DateTime? connectedAt = null;
    string lastStatus = "";

    static readonly string[] DefaultKeywords = {
        "zapret","winws","goodbyedpi","spoofdpi","byedpi","nfqws","tpws",
        "dpi_bypass","dpi-bypass","ciadpi","zaborona","anticenz","greentunnel"
    };

    static readonly SolidColorBrush Green = MakeBrush("#22C55E");
    static readonly SolidColorBrush Gray = MakeBrush("#6B7280");
    static readonly SolidColorBrush Light = MakeBrush("#F4F5F9");
    static readonly Brush GreenBtn = MakeGradient("#26D07C", "#17A34A");
    static readonly Brush RedBtn = MakeGradient("#FF6B6B", "#E23C3C");
    static readonly Brush VioletBtn = MakeGradient("#A78BFA", "#7C3AED");

    static SolidColorBrush MakeBrush(string hex) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    static LinearGradientBrush MakeGradient(string top, string bottom)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(top), 0));
        g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(bottom), 1));
        return g;
    }

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    public MainWindow()
    {
        InitializeComponent();

        PowerBtn.Click += (s, e) => { if (TunnelUp()) Disconnect(); else Connect(); };
        HappBtn.Click += (s, e) => OpenClient();
        IpBtn.Click += (s, e) => RefreshIp();
        LogBtn.Click += (s, e) => ToggleLog();
        ClearLogBtn.Click += (s, e) => LogBox.Document.Blocks.Clear();
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

        // Автоподбор пути к VPN-клиенту, если он не указан или файл не существует
        if (current != null && (string.IsNullOrEmpty(current.clientPath) || !File.Exists(current.clientPath)))
        {
            var detected = ConfigStore.DetectClientPath();
            if (!string.IsNullOrEmpty(detected))
            {
                current.clientPath = detected;
                try { ConfigStore.Save(config); } catch { }
                Log("Найден VPN-клиент: " + detected);
            }
        }

        var logoImg = Ui.LoadLogo();
        if (logoImg != null) Logo.Source = logoImg;

        SetupTray();
        SetupAnim();
        SetupGuard();
        RegisterGlobalHotkey();
        ApplyLang();
        ApplyLogState();
        UpdateStatus();
        RefreshIp();

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
            LogRow.MinHeight = 70;
            Height = 720;
        }
        else
        {
            LogCard.Visibility = Visibility.Collapsed;
            LogSplitter.Visibility = Visibility.Collapsed;
            SpacerRow.Height = new GridLength(0);
            LogRow.Height = new GridLength(0);
            LogRow.MinHeight = 0;
            Height = 560;
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
        ClearLogBtn.Content = Lang.T("clear_log");
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
        if (res.version == null) return;

        // спросить, обновить ли сейчас
        bool update = Ui.ShowConfirm(this, Lang.T("update_available") + ": v" + res.version + ". " + Lang.T("update_now"), "Pconnect");
        if (!update) return;

        Ui.ShowToast("Pconnect", Lang.T("downloading_update"), "#8B5CF6");
        var installer = await DownloadInstallerAsync(res.downloadUrl);
        if (installer == null)
        {
            Log(Lang.T("update_download_failed"));
            Ui.ShowMessage(this, Lang.T("update_download_failed"), warning: true);
            return;
        }

        Log("Запуск установщика обновления...");
        try
        {
            Process.Start(new ProcessStartInfo(installer, "/SILENT /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
        }
        catch { }
        if (tray != null) { tray.Visible = false; tray.Dispose(); }
        Application.Current.Shutdown();
    }

    async System.Threading.Tasks.Task<string> DownloadInstallerAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        try
        {
            var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Pconnect-Update-" + Guid.NewGuid().ToString("N") + ".exe");
            Log("Скачивание обновления...");
            using var hc = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            hc.DefaultRequestHeaders.UserAgent.ParseAdd("Pconnect");
            var bytes = await hc.GetByteArrayAsync(url);
            await System.IO.File.WriteAllBytesAsync(tmp, bytes);
            Log("Обновление скачано.");
            return tmp;
        }
        catch
        {
            return null;
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
        iconGreen = MakeIcon("#22C55E");
        iconGray = MakeIcon("#6B7280");

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
        // пульс точки — «дыхание»
        var scale = new ScaleTransform(1, 1);
        Dot.RenderTransformOrigin = new Point(0.5, 0.5);
        Dot.RenderTransform = scale;
        var pulse = new DoubleAnimation(1.0, 1.55, TimeSpan.FromMilliseconds(650))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);

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
            if (Prefs.Get("KillSwitch") && stateOn && current != null && (!TunnelUp() || !ClientRunning()))
            {
                stateOn = false;
                HandleVpnDrop();
            }
        };
        guard.Start();
    }

    void HandleVpnDrop()
    {
        bool userDisconnected = Ui.ShowConfirm(this, Lang.T("vpn_dropped_confirm"), "Pconnect");
        if (userDisconnected)
        {
            // пользователь отключил VPN сам — просто очистить состояние
            Log("VPN отключён пользователем.");
            RestoreInterfering();
            connectedAt = null;
            TimerText.Text = "";
            UpdateStatus();
            RefreshIp();
        }
        else
        {
            // обрыв — переподключить
            Log("Обнаружен обрыв VPN — переподключаю…");
            Connect();
        }
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

    void FadeIn(FrameworkElement el)
    {
        el.Opacity = 0;
        el.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)));
    }

    StackPanel BuildSpinnerContent(string text)
    {
        var spinner = new System.Windows.Shapes.Path
        {
            Stroke = Brushes.White,
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 18,
            Height = 18,
            Data = Geometry.Parse("M 9,1.5 A 7.5,7.5 0 1 1 1.5,9")
        };
        spinner.RenderTransformOrigin = new Point(0.5, 0.5);
        var rotate = new RotateTransform(0);
        spinner.RenderTransform = rotate;
        rotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.85)) { RepeatBehavior = RepeatBehavior.Forever });

        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(spinner);
        stack.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        return stack;
    }

    void Log(string msg)
    {
        try
        {
            var para = new Paragraph { Margin = new Thickness(0) };
            para.Inlines.Add(new Run(DateTime.Now.ToString("dd.MM HH:mm:ss") + "  " + msg) { Foreground = new SolidColorBrush(LogColor(msg)) });
            LogBox.Document.Blocks.Add(para);
            while (LogBox.Document.Blocks.Count > 500)
                LogBox.Document.Blocks.Remove(LogBox.Document.Blocks.FirstBlock);
            LogBox.ScrollToEnd();
            DoEvents();
        }
        catch { }
    }

    static Color LogColor(string msg)
    {
        var m = (msg ?? "").ToLower();
        if (m.Contains("ошибк") || m.Contains("не удалось") || m.Contains("не найден"))
            return Color.FromRgb(0xFF, 0x6B, 0x6B); // красный — ошибка
        if (m.Contains("обрыв") || m.Contains("разорван") || m.Contains("переподключ"))
            return Color.FromRgb(0xFF, 0xB0, 0x4D); // оранжевый — предупреждение
        if (m.Contains("готов") || m.Contains("поднят") || m.Contains("включена") || m.Contains("отключены")
            || m.Contains("возвращены") || m.Contains("определ") || m.Contains("найден") || m.Contains("перезапущены") || m.Contains("успешно"))
            return Color.FromRgb(0x4A, 0xDE, 0x80); // зелёный — успех
        return Color.FromRgb(0xB8, 0xC0, 0xCC); // серый — обычное
    }

    bool TunnelUp()
    {
        if (current == null || string.IsNullOrEmpty(current.adapter)) return false;
        return NetworkInterface.GetAllNetworkInterfaces()
            .Any(n => n.Name.Equals(current.adapter, StringComparison.OrdinalIgnoreCase)
                   && n.OperationalStatus == OperationalStatus.Up);
    }

    bool ClientRunning()
    {
        try
        {
            if (current == null || string.IsNullOrEmpty(current.clientPath)) return true;
            var name = Path.GetFileNameWithoutExtension(current.clientPath);
            if (string.IsNullOrEmpty(name)) return true;
            return Process.GetProcessesByName(name).Length > 0;
        }
        catch { return true; }
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

    // Синхронный запуск PowerShell с ожиданием завершения и выводом ошибки в журнал
    void RunPowerShellSync(string script)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"" + script + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            });
            if (p == null) return;
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            if (!string.IsNullOrWhiteSpace(err))
                Log("Ошибка команды: " + err.Trim());
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

    async Task FixRoutingAsync()
    {
        try
        {
            // небольшая пауза: дать TUN-адаптеру полностью подняться и создать маршруты
            await Task.Delay(1500);
            RunPowerShellSync("Set-NetIPInterface -InterfaceAlias '" + current.adapter + "' -AddressFamily IPv4 -IgnoreDefaultRoutes Disabled");
            Log("Маршрутизация включена.");
        }
        catch (Exception ex) { Log("Не удалось настроить маршрут: " + ex.Message); }
    }

    bool refreshingIp = false;

    async void RefreshIp(string avoidIp = null)
    {
        if (refreshingIp) return;
        refreshingIp = true;
        try
        {
            for (int attempt = 0; attempt < 15; attempt++)
            {
                if (attempt > 0) await Task.Delay(1500);
                var (ip, countryText, countryCode) = await FetchIpAsync();
                if (ip != null && (avoidIp == null || ip != avoidIp))
                {
                    IpText.Text = Lang.T("your_ip") + ": " + ip;
                    CountryText.Text = countryText;
                    LoadFlag(countryCode);
                    FadeIn(CountryText);
                    FadeIn(IpText);
                    Log("Текущий IP: " + ip);
                    return;
                }
            }
            IpText.Text = Lang.T("your_ip") + ": —";
            CountryText.Text = "";
            LoadFlag("");
            FadeIn(IpText);
        }
        finally { refreshingIp = false; }
    }

    string CurrentIpText()
    {
        var m = Regex.Match(IpText.Text ?? "", @"\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}");
        return m.Success ? m.Value : null;
    }

    async Task<(string ip, string countryText, string countryCode)> FetchIpAsync()
    {
        try
        {
            using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var ip = (await hc.GetStringAsync("https://api.ipify.org")).Trim();
            string country = null, city = null;
            try
            {
                var info = await hc.GetStringAsync("https://ipinfo.io/" + ip + "/json");
                var node = JsonNode.Parse(info);
                country = (string)node?["country"];
                city = (string)node?["city"];
            }
            catch { }
            var cc = string.IsNullOrEmpty(country) ? "" : country.ToUpper();
            var ct = string.IsNullOrEmpty(cc)
                ? ""
                : (string.IsNullOrEmpty(city) ? cc : city + ", " + cc);
            return (ip, ct, cc);
        }
        catch { return (null, null, ""); }
    }

    void LoadFlag(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            FlagBorder.Visibility = Visibility.Collapsed;
            return;
        }
        try
        {
            code = code.ToLower();
            var asm = typeof(App).Assembly;
            var stream = asm.GetManifestResourceStream("Pconnect.Flags." + code + ".png");
            if (stream == null)
            {
                var name = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("." + code + ".png", StringComparison.OrdinalIgnoreCase));
                stream = name == null ? null : asm.GetManifestResourceStream(name);
            }
            if (stream == null)
            {
                FlagBorder.Visibility = Visibility.Collapsed;
                return;
            }
            using (stream)
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.StreamSource = stream;
                bmp.EndInit();
                bmp.Freeze();
                FlagImg.Source = bmp;
            }
            FlagBorder.Visibility = Visibility.Visible;
        }
        catch
        {
            FlagBorder.Visibility = Visibility.Collapsed;
        }
    }

    async void OpenClient()
    {
        if (!string.IsNullOrEmpty(current?.clientPath) && File.Exists(current.clientPath))
        {
            Process.Start(new ProcessStartInfo(current.clientPath) { UseShellExecute = true });
            await Task.Delay(500);
            ShowClientWindow();
        }
        else
            Log("Не указан clientPath в профиле.");
    }

    // применяет действие ко всем окнам процесса VPN-клиента (включая скрытые)
    void ForEachClientWindow(Action<IntPtr> action)
    {
        try
        {
            if (current == null || string.IsNullOrEmpty(current.clientPath)) return;
            var name = Path.GetFileNameWithoutExtension(current.clientPath);
            if (string.IsNullOrEmpty(name)) return;
            var pids = Process.GetProcessesByName(name).Select(p => p.Id).ToHashSet();
            if (pids.Count == 0) return;
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pids.Contains((int)pid))
                {
                    var sb = new System.Text.StringBuilder(256);
                    GetWindowText(hWnd, sb, 256);
                    if (sb.Length > 0) action(hWnd); // только окна с заголовком (не служебные)
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
    }

    void HideClientWindow()
    {
        ForEachClientWindow(h => ShowWindow(h, 0)); // SW_HIDE — полностью скрыть
    }

    // опрашивает появление окна клиента и прячет его (пока идёт подключение)
    async Task HideClientWindowPollAsync()
    {
        for (int i = 0; i < 20; i++)
        {
            HideClientWindow();
            await Task.Delay(120);
        }
    }

    void ShowClientWindow()
    {
        ForEachClientWindow(h => ShowWindow(h, 5)); // SW_SHOW
    }

    // перезапускает браузеры после подключения, чтобы они работали через VPN
    async Task RestartBrowsersAsync()
    {
        try
        {
            var names = new[] { "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "browser", "yandex" };
            int restarted = 0;
            foreach (var name in names)
            {
                var procs = Process.GetProcessesByName(name);
                if (procs.Length == 0) continue;

                var exePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in procs)
                {
                    try { var f = p.MainModule?.FileName; if (!string.IsNullOrEmpty(f)) exePaths.Add(f); } catch { }
                }

                foreach (var p in procs) { try { p.Kill(); } catch { } }
                await Task.Delay(1000); // дать процессу полностью завершиться

                foreach (var exe in exePaths)
                {
                    try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); restarted++; }
                    catch { }
                }
            }
            if (restarted > 0)
                Log("Браузеры перезапущены (" + restarted + ").");
        }
        catch { }
    }

    // ждёт, пока внешний IP сменится (т.е. VPN-маршрут реально активировался)
    async Task WaitForIpChangeAsync(string beforeIp)
    {
        try
        {
            for (int i = 0; i < 15; i++)
            {
                var (ip, _, _) = await FetchIpAsync();
                if (ip != null && (string.IsNullOrEmpty(beforeIp) || ip != beforeIp))
                    return;
                await Task.Delay(1500);
            }
        }
        catch { }
    }

    void OpenSettings()
    {
        var sw = new SettingsWindow { Owner = this };
        sw.ShowDialog();
        config = ConfigStore.Load();
        current = config.profiles.FirstOrDefault(p => p.name == config.defaultProfile)
               ?? config.profiles.FirstOrDefault();
        ApplyLang();
        UpdateStatus();
    }

    // ---------- статус ----------
    void SetWorking(bool working, string text = null)
    {
        Prog.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        if (working && text != null)
        {
            PowerBtn.Content = BuildSpinnerContent(text);
            PowerBtn.Background = VioletBtn;
        }
    }

    void UpdateStatus()
    {
        bool on = TunnelUp();
        stateOn = on;
        string status = on ? Lang.T("connected") : Lang.T("disconnected");
        StatusText.Text = status;
        Dot.Fill = on ? Green : Gray;
        StatusText.Foreground = Light;
        if (on)
        {
            PowerBtn.Content = Lang.T("disconnect");
            PowerBtn.Background = RedBtn;
        }
        else
        {
            PowerBtn.Content = Lang.T("connect");
            PowerBtn.Background = GreenBtn;
        }
        if (status != lastStatus)
        {
            lastStatus = status;
            FadeIn(StatusText);
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
    async void Connect()
    {
        if (current == null) return;
        PowerBtn.IsEnabled = false;
        stateOn = false;
        var beforeIp = CurrentIpText();
        try
        {
            Log("================ ЗАПУСК VPN ================");
            SetWorking(true, Lang.T("connecting"));
            StopInterfering();
            Log("Команда подключения: " + current.connect);
            RunAction(current.connect);
            await HideClientWindowPollAsync();

            if (!string.IsNullOrEmpty(current.adapter))
            {
                for (int i = 0; i < 50; i++)
                {
                    if (TunnelUp()) break;
                    await Task.Delay(750);
                }
                if (!TunnelUp())
                {
                    Log("Не удалось подключиться — проверьте настройки профиля.");
                    UpdateStatus();
                    return;
                }
                Log("Туннель «" + current.adapter + "» поднят.");
                HideClientWindow();
                if (current.fixRouting) await FixRoutingAsync();
            }
            else
            {
                Log("В профиле не указан адаптер — статус не отслеживается.");
                await Task.Delay(3000);
            }

            // после подключения — автоопределение адаптера, если он пустой или не существует
            var adapterValid = !string.IsNullOrEmpty(current.adapter) &&
                NetworkInterface.GetAllNetworkInterfaces().Any(n => n.Name.Equals(current.adapter, StringComparison.OrdinalIgnoreCase));
            if (!adapterValid)
            {
                var detected = ConfigStore.DetectAdapterName();
                if (!string.IsNullOrEmpty(detected))
                {
                    current.adapter = detected;
                    try { ConfigStore.Save(config); } catch { }
                    Log("Адаптер определён: " + detected);
                    if (current.fixRouting) await FixRoutingAsync();
                }
            }

            UpdateStatus();
            RefreshIp(beforeIp);
            connectedAt = DateTime.Now;
            await WaitForIpChangeAsync(beforeIp); // дождаться, пока VPN-маршрут реально активируется
            RunPowerShellSync("ipconfig /flushdns"); // сбросить DNS-кэш
            await RestartBrowsersAsync();
            Ui.PlaySound(true);
            Ui.ShowToast("Pconnect", Lang.T("connected_toast"), "#22C55E");
            Log("Готово.");
        }
        catch (Exception ex)
        {
            Log("Ошибка подключения: " + ex.Message);
            UpdateStatus();
        }
        finally
        {
            PowerBtn.IsEnabled = true;
        }
    }

    async void Disconnect()
    {
        if (current == null) return;
        PowerBtn.IsEnabled = false;
        stateOn = false; // чтобы kill switch не срабатывал во время отключения
        try
        {
            Log("================ ОСТАНОВКА VPN ================");
            if (TunnelUp())
            {
                SetWorking(true, Lang.T("disconnecting"));
                Log("Команда отключения: " + current.disconnect);
                RunAction(current.disconnect);
                await HideClientWindowPollAsync();
                for (int i = 0; i < 25; i++)
                {
                    if (!TunnelUp()) break;
                    await Task.Delay(500);
                }
            }
            else
            {
                Log("VPN уже не подключён.");
            }
            RestoreInterfering();
            TimerText.Text = "";
            connectedAt = null;
            UpdateStatus();
            await Task.Delay(800);
            RefreshIp();
            Ui.PlaySound(false);
            Ui.ShowToast("Pconnect", Lang.T("disconnected_toast"), "#6B7280");
            Log("Готово.");
        }
        catch (Exception ex)
        {
            Log("Ошибка отключения: " + ex.Message);
            UpdateStatus();
        }
        finally
        {
            PowerBtn.IsEnabled = true;
        }
    }
}
