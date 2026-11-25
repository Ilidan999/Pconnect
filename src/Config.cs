using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Pconnect;

public class Profile
{
    public string name { get; set; } = "";
    public string connect { get; set; } = "";
    public string disconnect { get; set; } = "";
    public string adapter { get; set; } = "";
    public string clientPath { get; set; } = "";
    public bool fixRouting { get; set; }
    public List<string> interferingServices { get; set; } = new();
    public List<string> interferingProcesses { get; set; } = new();
    public List<string> interferingKeywords { get; set; } = new();
}

public class AppConfig
{
    public string defaultProfile { get; set; } = "";
    public string updateRepo { get; set; } = "";
    public List<Profile> profiles { get; set; } = new();
}

public static class ConfigStore
{
    public static string AppDir => AppContext.BaseDirectory;
    // Настройки хранятся в профиле пользователя (%AppData%), а не рядом с exe:
    // это работает и в Program Files, и для каждого пользователя отдельно.
    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pconnect");
    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var txt = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(txt) ?? new AppConfig();
            }
        }
        catch { }

        // Запасной вариант: config.json рядом с exe (первый запуск после установки)
        try
        {
            var legacy = Path.Combine(AppDir, "config.json");
            if (File.Exists(legacy))
            {
                var txt = File.ReadAllText(legacy);
                return JsonSerializer.Deserialize<AppConfig>(txt) ?? new AppConfig();
            }
        }
        catch { }

        return new AppConfig();
    }

    public static void Save(AppConfig cfg)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            var opt = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, opt));
        }
        catch { }
    }

    // Автоподбор пути к VPN-клиенту: по обработчику протокола happ:// в реестре,
    // затем по запущенному процессу Happ.
    public static string DetectClientPath()
    {
        try
        {
            var cmd = Registry.GetValue(@"HKEY_CLASSES_ROOT\happ\shell\open\command", "", null) as string;
            if (!string.IsNullOrEmpty(cmd))
            {
                var path = ExtractExePath(cmd);
                if (File.Exists(path)) return path;
            }
        }
        catch { }

        try
        {
            foreach (var p in Process.GetProcessesByName("Happ"))
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                }
                catch { }
            }
        }
        catch { }

        return "";
    }

    static string ExtractExePath(string command)
    {
        command = (command ?? "").Trim();
        if (command.StartsWith("\""))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? command.Substring(1, end - 1) : command;
        }
        var parts = command.Split(new[] { ' ' }, 2);
        return parts[0];
    }

    // Автоподбор имени VPN-адаптера: по названию (happ/xray/wireguard/...) или по типу Tunnel,
    // исключая системные интерфейсы Windows (Teredo, 6to4, IP-HTTPS, isatap, Microsoft...).
    public static string DetectAdapterName()
    {
        var specific = new[] { "happ", "xray", "wireguard", "openvpn", "wintun", "tap-windows", "nordlynx", "vpn" };
        var exclude = new[]
        {
            "microsoft", "teredo", "6to4", "isatap", "ip-https", "pseudo-interface",
            "platform interface", "loopback", "bluetooth", "wi-fi", "directaccess", "debug"
        };
        var all = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => !exclude.Any(x => n.Name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();

        // 1) по имени конкретного VPN-адаптера
        var named = all.Where(n => specific.Any(k => n.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        // 2) иначе — по типу Tunnel (уже без системных интерфейсов)
        var tunnel = all.Where(n => n.NetworkInterfaceType == NetworkInterfaceType.Tunnel).ToList();
        var cands = named.Count > 0 ? named : tunnel;
        if (cands.Count == 0) return "";

        var up = cands.FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up) ?? cands[0];
        return up.Name;
    }
}
