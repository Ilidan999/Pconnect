using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Pconnect;

// ---------- локализация ----------
static class Lang
{
    static readonly Dictionary<string, (string ru, string en)> Dict = new()
    {
        ["subtitle"] = ("Универсальный помощник VPN", "Universal VPN helper"),
        ["disconnected"] = ("Не подключено", "Disconnected"),
        ["connected"] = ("Подключено", "Connected"),
        ["connect"] = ("Включить VPN", "Connect VPN"),
        ["disconnect"] = ("Выключить VPN", "Disconnect VPN"),
        ["hint"] = ("После подключения просто откройте любой браузер", "After connecting, just open any browser"),
        ["open_vpn"] = ("Открыть VPN", "Open VPN"),
        ["check_ip"] = ("Проверить IP", "Check IP"),
        ["log"] = ("Журнал", "Log"),
        ["open_app"] = ("Открыть Pconnect", "Open Pconnect"),
        ["settings"] = ("Настройки", "Settings"),
        ["exit"] = ("Выход", "Exit"),
        ["connected_toast"] = ("VPN подключён", "VPN connected"),
        ["disconnected_toast"] = ("VPN выключен", "VPN disconnected"),
        ["update_available"] = ("Доступна новая версия", "New version available"),
        ["tray_on"] = ("Pconnect — подключено", "Pconnect — connected"),
        ["tray_off"] = ("Pconnect — не подключено", "Pconnect — disconnected"),
        ["your_ip"] = ("Ваш IP", "Your IP"),
        ["minimize"] = ("Свернуть", "Minimize"),
        ["close"] = ("Закрыть", "Close"),
        ["log_toggle"] = ("Журнал", "Log"),
        ["connecting"] = ("Подключение…", "Connecting…"),
        ["disconnecting"] = ("Отключение…", "Disconnecting…"),
        ["language"] = ("Язык", "Language"),
        ["add"] = ("＋ Добавить", "＋ Add"),
        ["sec_profile"] = ("ПРОФИЛЬ", "PROFILE"),
        ["sec_connection"] = ("ПОДКЛЮЧЕНИЕ", "CONNECTION"),
        ["sec_extra"] = ("ДОПОЛНИТЕЛЬНО", "ADDITIONAL"),
        ["name"] = ("Название", "Name"),
        ["connect_cmd"] = ("Команда подключения", "Connect command"),
        ["disconnect_cmd"] = ("Команда отключения", "Disconnect command"),
        ["adapter"] = ("Адаптер (для статуса)", "Adapter (for status)"),
        ["detect"] = ("Подобрать", "Detect"),
        ["client_path"] = ("Путь к приложению", "Application path"),
        ["fix_routing"] = ("Исправлять маршрутизацию (IgnoreDefaultRoutes)", "Fix routing (IgnoreDefaultRoutes)"),
        ["services"] = ("Службы для отключения (через запятую)", "Services to disable (comma-separated)"),
        ["processes"] = ("Процессы для завершения (через запятую)", "Processes to stop (comma-separated)"),
        ["kill_switch"] = ("Защита от обрыва — авто-переподключение при падении VPN", "Kill switch — auto-reconnect if VPN drops"),
        ["save"] = ("Сохранить", "Save"),
        ["cancel"] = ("Закрыть", "Close"),
        ["saved"] = ("Настройки сохранены.", "Settings saved."),
        ["no_profile_save"] = ("Нет профиля для сохранения.", "No profile to save."),
        ["adapter_not_found"] = ("VPN-адаптер не найден. Подключите VPN и повторите.", "VPN adapter not found. Connect VPN and retry."),
        ["sec_data"] = ("ДАННЫЕ", "DATA"),
        ["export"] = ("Экспорт", "Export"),
        ["import"] = ("Импорт", "Import"),
        ["exported"] = ("Настройки экспортированы.", "Settings exported."),
        ["imported"] = ("Настройки импортированы.", "Settings imported."),
        ["import_error"] = ("Не удалось импортировать файл настроек.", "Failed to import the settings file."),
        ["client_not_found"] = ("VPN-клиент не найден.", "VPN client not found."),
    };

    public static bool English => Prefs.Get("Lang");

    public static string T(string key) =>
        Dict.TryGetValue(key, out var pair) ? (English ? pair.en : pair.ru) : key;
}

// ---------- автообновление ----------
static class UpdateChecker
{
    class ReleaseInfo
    {
        public string tag_name { get; set; }
        public string html_url { get; set; }
    }

    public static async Task<(string version, string url)> CheckAsync(string repo, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(repo)) return default;
        try
        {
            using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            hc.DefaultRequestHeaders.UserAgent.ParseAdd("Pconnect");
            var json = await hc.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest");
            var rel = JsonSerializer.Deserialize<ReleaseInfo>(json);
            if (rel == null || string.IsNullOrEmpty(rel.tag_name)) return default;
            if (IsNewer(rel.tag_name, currentVersion)) return (rel.tag_name, rel.html_url);
            return default;
        }
        catch { return default; }
    }

    static bool IsNewer(string tag, string current)
    {
        var a = Parse(tag);
        var b = Parse(current);
        for (int i = 0; i < 3; i++)
        {
            if (a[i] > b[i]) return true;
            if (a[i] < b[i]) return false;
        }
        return false;
    }

    static int[] Parse(string v)
    {
        var parts = (v ?? "").Trim().TrimStart('v', 'V').Split('.');
        var r = new int[3];
        for (int i = 0; i < 3; i++)
            r[i] = i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;
        return r;
    }
}
