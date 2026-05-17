using System.IO;
using System.Text.Json;

namespace NetFence.App.Services;

public static class SettingsService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetFence",
        "settings.json");

    private static SettingsData? _current;

    public static string Theme
    {
        get => Load().Theme;
        set { Load().Theme = value; Save(); }
    }

    public static string Language
    {
        get => Load().Language;
        set { Load().Language = value; Save(); }
    }

    public static bool GuardianEnabled
    {
        get => Load().GuardianEnabled;
        set { Load().GuardianEnabled = value; Save(); }
    }

    private static SettingsData Load()
    {
        if (_current is not null) return _current;
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                _current = JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
            }
            else
            {
                _current = new SettingsData();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsService load failed: {ex.Message}");
            _current = new SettingsData();
        }
        return _current;
    }

    private static void Save()
    {
        if (_current is null) return;
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_current));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsService save failed: {ex.Message}");
        }
    }

    private sealed class SettingsData
    {
        public string Theme { get; set; } = "system";
        public string Language { get; set; } = "";
        public bool GuardianEnabled { get; set; } = true;
    }
}
