using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

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
        DetectClientBtn.Click += (s, e) => DetectClientPath();
        AddBtn.Click += (s, e) => AddProfile();
        DelBtn.Click += (s, e) => DeleteProfile();
        SaveBtn.Click += (s, e) => Save();
        CancelBtn.Click += (s, e) => Close();
        SWClose.Click += (s, e) => Close();
        ExportBtn.Click += (s, e) => ExportConfig();
        ImportBtn.Click += (s, e) => ImportConfig();
        FFixRouting.Click += (s, e) => Ui.PlayClick();
        FKillSwitch.Click += (s, e) => Ui.PlayClick();
        RbRu.Checked += (s, e) => SetLanguage(false);
        RbEn.Checked += (s, e) => SetLanguage(true);
        PreviewMouseLeftButtonDown += OnDrag;
        Loaded += OnLoaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        config = ConfigStore.Load();
        profiles = config.profiles ?? new List<Profile>();
        FKillSwitch.IsChecked = Prefs.Get("KillSwitch");
        RbRu.IsChecked = !Lang.English;
        RbEn.IsChecked = Lang.English;
        ApplyLang();
        RefreshList();
    }

    void SetLanguage(bool english)
    {
        Prefs.Set("Lang", english);
        ApplyLang();
    }

    void ApplyLang()
    {
        TitleText.Text = Lang.T("settings");
        LangLabel.Text = Lang.T("language");
        SecProfile.Text = Lang.T("sec_profile");
        SecConnection.Text = Lang.T("sec_connection");
        SecExtra.Text = Lang.T("sec_extra");
        SecData.Text = Lang.T("sec_data");
        ExportBtn.Content = Lang.T("export");
        ImportBtn.Content = Lang.T("import");
        NameLabel.Text = Lang.T("name");
        ConnectLabel.Text = Lang.T("connect_cmd");
        DisconnectLabel.Text = Lang.T("disconnect_cmd");
        AdapterLabel.Text = Lang.T("adapter");
        ClientPathLabel.Text = Lang.T("client_path");
        ServicesLabel.Text = Lang.T("services");
        ProcessesLabel.Text = Lang.T("processes");
        FFixRouting.Content = Lang.T("fix_routing");
        FKillSwitch.Content = Lang.T("kill_switch");
        AddBtn.Content = Lang.T("add");
        DetectBtn.Content = Lang.T("detect");
        DetectClientBtn.Content = Lang.T("detect");
        SaveBtn.Content = Lang.T("save");
        CancelBtn.Content = Lang.T("cancel");
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
                t == "RadioButton" || t == "ScrollBar" || t == "GridSplitter" || t == "Thumb" || t == "RepeatButton")
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
        if (profiles.Count > 0)
        {
            var def = profiles.FirstOrDefault(p => p.name == config.defaultProfile);
            ProfileCombo.SelectedItem = def != null ? def.name : profiles[0].name;
        }
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
        var name = ConfigStore.DetectAdapterName();
        if (string.IsNullOrEmpty(name))
        {
            Ui.ShowMessage(this, Lang.T("adapter_not_found"), warning: true);
            return;
        }
        FAdapter.Text = name;
    }

    void DetectClientPath()
    {
        var path = ConfigStore.DetectClientPath();
        if (string.IsNullOrEmpty(path))
        {
            Ui.ShowMessage(this, Lang.T("client_not_found"), warning: true);
            return;
        }
        FClientPath.Text = path;
    }

    void SyncCurrentProfile()
    {
        if (current == null) return;
        current.name = FName.Text;
        current.connect = FConnect.Text;
        current.disconnect = FDisconnect.Text;
        current.adapter = FAdapter.Text;
        current.clientPath = FClientPath.Text;
        current.fixRouting = FFixRouting.IsChecked == true;
        current.interferingServices = SplitCsv(FServices.Text);
        current.interferingProcesses = SplitCsv(FProcesses.Text);
    }

    void Save()
    {
        if (current == null)
        {
            Ui.ShowMessage(this, Lang.T("no_profile_save"), warning: true);
            return;
        }
        SyncCurrentProfile();
        Prefs.Set("KillSwitch", FKillSwitch.IsChecked == true);
        config.profiles = profiles;
        ConfigStore.Save(config);
        RefreshList();
        Ui.ShowMessage(this, Lang.T("saved"));
    }

    void ExportConfig()
    {
        try
        {
            var dlg = new SaveFileDialog { FileName = "Pconnect-config.json", Filter = "JSON (*.json)|*.json", Title = Lang.T("export") };
            if (dlg.ShowDialog() != true) return;
            SyncCurrentProfile();
            config.profiles = profiles;
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
            Ui.ShowMessage(this, Lang.T("exported"));
        }
        catch
        {
            Ui.ShowMessage(this, Lang.T("import_error"), warning: true);
        }
    }

    void ImportConfig()
    {
        try
        {
            var dlg = new OpenFileDialog { Filter = "JSON (*.json)|*.json", Title = Lang.T("import") };
            if (dlg.ShowDialog() != true) return;
            var txt = File.ReadAllText(dlg.FileName);
            var imported = JsonSerializer.Deserialize<AppConfig>(txt);
            if (imported == null || imported.profiles == null || imported.profiles.Count == 0)
            {
                Ui.ShowMessage(this, Lang.T("import_error"), warning: true);
                return;
            }
            config = imported;
            profiles = config.profiles;
            ConfigStore.Save(config);
            RefreshList();
            Ui.ShowMessage(this, Lang.T("imported"));
        }
        catch
        {
            Ui.ShowMessage(this, Lang.T("import_error"), warning: true);
        }
    }

    static List<string> SplitCsv(string s) =>
        (s ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
}
