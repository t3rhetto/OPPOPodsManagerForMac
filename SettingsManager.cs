using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OppoPodsManager;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class AppSettingsContext : JsonSerializerContext { }

/// <summary>Cross-platform settings storage using JSON file</summary>
public static class SettingsManager
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OppoPodsWin", "settings.json");

    private static Dictionary<string, string> _cache = new();
    private static DateTime _lastRead = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);

    private static Dictionary<string, string> Load()
    {
        if (DateTime.Now - _lastRead < CacheDuration && _cache.Count > 0)
            return _cache;
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                _cache = JsonSerializer.Deserialize(json, AppSettingsContext.Default.DictionaryStringString) ?? new();
            }
            else
                _cache = new Dictionary<string, string>();
        }
        catch
        {
            _cache = new Dictionary<string, string>();
        }
        _lastRead = DateTime.Now;
        return _cache;
    }

    private static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_cache, AppSettingsContext.Default.DictionaryStringString));
        }
        catch { }
    }

    public static string? GetString(string key, string? defaultValue = null)
    {
        var data = Load();
        return data.TryGetValue(key, out var v) ? v : defaultValue;
    }

    public static void SetString(string key, string? value)
    {
        var data = Load();
        if (value != null)
            data[key] = value;
        else
            data.Remove(key);
        Save();
    }

    public static bool GetBool(string key, bool defaultValue = false)
    {
        var val = GetString(key);
        return val switch { "1" or "true" => true, "0" or "false" => false, _ => defaultValue };
    }

    public static void SetBool(string key, bool value) =>
        SetString(key, value ? "1" : null);

    public static int GetInt(string key, int defaultValue = 0)
    {
        var val = GetString(key);
        return int.TryParse(val, out var n) ? n : defaultValue;
    }

    public static void SetInt(string key, int value) =>
        SetString(key, value.ToString());
}
