using System.Windows;
using Microsoft.Win32;

namespace NetFence.App.Services;

public static class ThemeService
{
    public const string SystemKey = "system";
    public const string DarkKey = "dark";
    public const string LightKey = "light";

    private const string DarkDictUri = "Themes/DarkTheme.xaml";
    private const string LightDictUri = "Themes/LightTheme.xaml";

    private static ResourceDictionary? _darkDict;
    private static ResourceDictionary? _lightDict;

    public static void Apply(string theme)
    {
        var resolved = theme == SystemKey ? ReadSystemTheme() : theme;
        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;

        _darkDict ??= new ResourceDictionary { Source = new Uri(DarkDictUri, UriKind.Relative) };
        _lightDict ??= new ResourceDictionary { Source = new Uri(LightDictUri, UriKind.Relative) };

        merged.Remove(_darkDict);
        merged.Remove(_lightDict);
        merged.Add(resolved == DarkKey ? _darkDict : _lightDict);

        SettingsService.Theme = theme;
    }

    public static string GetEffectiveTheme(string stored)
    {
        return stored == SystemKey ? ReadSystemTheme() : stored;
    }

    private static string ReadSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v)
                return v == 0 ? DarkKey : LightKey;
        }
        catch { }
        return LightKey;
    }
}
