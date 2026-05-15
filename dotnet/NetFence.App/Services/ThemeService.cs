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

    public static event Action? ThemeChanged;

    public static void Apply(string theme)
    {
        if (theme is not SystemKey and not DarkKey and not LightKey)
            throw new ArgumentException($"Unknown theme: '{theme}'.", nameof(theme));

        if (System.Windows.Application.Current is not { } app)
            return;

        var resolved = theme == SystemKey ? ReadSystemTheme() : theme;
        var merged = app.Resources.MergedDictionaries;

        _darkDict ??= new ResourceDictionary { Source = new Uri(DarkDictUri, UriKind.Relative) };
        _lightDict ??= new ResourceDictionary { Source = new Uri(LightDictUri, UriKind.Relative) };

        merged.Remove(_darkDict);
        merged.Remove(_lightDict);
        merged.Add(resolved == DarkKey ? _darkDict : _lightDict);

        SettingsService.Theme = theme;
        ThemeChanged?.Invoke();
    }

    public static string GetEffectiveTheme(string stored)
    {
        return stored == SystemKey ? ReadSystemTheme() : stored;
    }

    public static void ApplyTitleBar(Window window)
    {
        var effective = GetEffectiveTheme(SettingsService.Theme);
        SetTitleBarDarkMode(window, effective == DarkKey);
    }

    private static void SetTitleBarDarkMode(Window window, bool dark)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int useDark = dark ? 1 : 0;
        // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (Win10 20H1+), fallback 19 (Win10 1809+)
        if (DwmSetWindowAttribute(hwnd, 20, ref useDark, 4) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref useDark, 4);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

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
