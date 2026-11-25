using System.Globalization;
using System.Windows;

namespace FoxuMarsDisplay.Services;

public static class LocalizationService
{
    private static readonly string[] Supported = ["es", "en", "fr", "de", "pt"];
    private static ResourceDictionary? _activeDict;

    public static string CurrentLanguageCode { get; private set; } = "en";

    public static string ResolveLanguageCode(string? configured, CultureInfo? osCulture = null)
    {
        if (!string.IsNullOrWhiteSpace(configured) &&
            !configured.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            string norm = configured.Trim().ToLowerInvariant();
            if (Supported.Contains(norm))
                return norm;
        }

        var culture = osCulture ?? CultureInfo.CurrentUICulture;
        string iso = culture.TwoLetterISOLanguageName.ToLowerInvariant();
        return Supported.Contains(iso) ? iso : "en";
    }

    public static void ApplyLanguage(string? configured)
    {
        string code = ResolveLanguageCode(configured);
        CurrentLanguageCode = code;

        var app = System.Windows.Application.Current;
        if (app is null) return;

        var uri = new Uri($"pack://application:,,,/FoxuMarsDisplay;component/Resources/Strings.{code}.xaml", UriKind.Absolute);
        var newDict = new ResourceDictionary { Source = uri };

        if (_activeDict is not null)
            app.Resources.MergedDictionaries.Remove(_activeDict);

        app.Resources.MergedDictionaries.Add(newDict);
        _activeDict = newDict;
    }

    public static string T(string key)
    {
        var app = System.Windows.Application.Current;
        if (app?.TryFindResource(key) is string s)
            return s;
        return key;
    }

    public static string Format(string key, params object[] args)
    {
        string fmt = T(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, fmt, args);
        }
        catch
        {
            return fmt;
        }
    }
}
