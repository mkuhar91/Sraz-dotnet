using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Sraz.UI.Wpf.Localization;

internal sealed class AppSettings
{
    public string Language { get; set; } = string.Empty;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile));
                if (settings is not null && Localizer.SupportedLanguages.Contains(settings.Language))
                {
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        var detected = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Localizer.Croatian
            ? Localizer.Croatian
            : Localizer.English;
        return new AppSettings { Language = detected };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SettingsFile)!);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
