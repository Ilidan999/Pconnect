using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Pconnect;

public partial class SettingsWindow : Window
{
    AppConfig config;
    List<Profile> profiles;
    Profile current;

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    public SettingsWindow()
    {
        InitializeComponent();
        ProfileCombo.SelectionChanged += (s, e) => LoadFields();
        DetectBtn.Click += (s, e) => DetectAdapter();
        AddBtn.Click += (s, e) => AddProfile();
        DelBtn.Click += (s, e) => DeleteProfile();
        SaveBtn.Click += (s, e) => Save();
        CancelBtn.Click += (s, e) => Close();
        SWClose.Click += (s, e) => Close();
        BackdropCombo.SelectionChanged += (s, e) => ApplyBackdropChoice();
        PreviewMouseLeftButtonDown += OnDrag;
        Loaded += OnLoaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        config = ConfigStore.Load();
        profiles = config.profiles ?? new List<Profile>();
        FKillSwitch.IsChecked = Prefs.Get("KillSwitch");

        BackdropCombo.ItemsSource = new[] { "Acrylic", "Mica", "Без эффекта" };
        BackdropCombo.SelectedIndex = Prefs.GetStr("Backdrop", "acrylic") switch
        {
            "mica" => 1,
            "none" => 2,
            _ => 0,
        };
        Glass.Apply(this, Surface, Prefs.GetStr("Backdrop", "acrylic"));

        RefreshList();
    }

    void ApplyBackdropChoice()
    {
        string mode = BackdropCombo.SelectedIndex switch
        {
            1 => "mica",
            2 => "none",
            _ => "acrylic",
        };
        Prefs.SetStr("Backdrop", mode);
        Glass.Apply(this, Surface, mode);
    }

    void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (HasInteractive(e.OriginalSource)) return;
        try
        {
            var h = new WindowInteropHelper(this).Handle;
            ReleaseCapture();
            SendMessage(h, 0xA1, (IntPtr)2, IntPtr.Zero);
        }
        catch { }
    }

    bool HasInteractive(object el)
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

    void RefreshList()
    {
        ProfileCombo.ItemsSource = profiles.Select(p => p.name).ToList();
        if (profiles.Count > 0) ProfileCombo.SelectedIndex = 0;
        else { current = null; ClearFields(); }
    }

    void LoadFields()
    {
        var name = ProfileCombo.SelectedItem as string;
        current = profiles.FirstOrDefault(p => p.name == name);
        if (current == null) { ClearFields(); return; }
        FName.Text = current.name;
        FConnect.Text = current.connect;
        FDisconnect.Text = current.disconnect;
        FAdapter.Text = current.adapter;
        FClientPath.Text = current.clientPath;
        FFixRouting.IsChecked = current.fixRouting;
        FServices.Text = string.Join(", ", current.interferingServices);
        FProcesses.Text = string.Join(", ", current.interferingProcesses);
    }

    void ClearFields()
    {
        FName.Text = FConnect.Text = FDisconnect.Text = FAdapter.Text = FClientPath.Text = "";
        FServices.Text = FProcesses.Text = "";
        FFixRouting.IsChecked = false;
    }

    void AddProfile()
    {
        profiles.Add(new Profile { name = "Новый профиль" });
        RefreshList();
        ProfileCombo.SelectedItem = "Новый профиль";
        LoadFields();
    }

    void DeleteProfile()
    {
        if (current == null || profiles.Count <= 1) return;
        var nm = current.name;
        profiles.RemoveAll(p => p.name == nm);
        current = null;
        RefreshList();
    }

    void DetectAdapter()
    {
        var keys = new[] { "tun", "tap", "vpn", "happ", "wireguard", "wintun", "openvpn" };
        var cands = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType == NetworkInterfaceType.Tunnel
                     || keys.Any(k => n.Name.ToLower().Contains(k)))
            .ToList();
        if (cands.Count == 0)
        {
            MessageBox.Show("VPN-адаптер не найден. Подключите VPN и повторите.", "Pconnect", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var up = cands.FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up) ?? cands[0];
        FAdapter.Text = up.Name;
    }

    void Save()
    {
        if (current == null)
        {
            MessageBox.Show("Нет профиля для сохранения.", "Pconnect", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        current.name = FName.Text;
        current.connect = FConnect.Text;
        current.disconnect = FDisconnect.Text;
        current.adapter = FAdapter.Text;
        current.clientPath = FClientPath.Text;
        current.fixRouting = FFixRouting.IsChecked == true;
        current.interferingServices = SplitCsv(FServices.Text);
        current.interferingProcesses = SplitCsv(FProcesses.Text);
        Prefs.Set("KillSwitch", FKillSwitch.IsChecked == true);
        config.profiles = profiles;
        ConfigStore.Save(config);
        RefreshList();
        MessageBox.Show("Настройки сохранены.", "Pconnect", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    static List<string> SplitCsv(string s) =>
        (s ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
}
