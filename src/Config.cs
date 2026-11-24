using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    public List<Profile> profiles { get; set; } = new();
}

public static class ConfigStore
{
    public static string AppDir => AppContext.BaseDirectory;
    public static string ConfigPath => Path.Combine(AppDir, "config.json");

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
        return new AppConfig();
    }

    public static void Save(AppConfig cfg)
    {
        try
        {
            var opt = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, opt));
        }
        catch { }
    }
}
