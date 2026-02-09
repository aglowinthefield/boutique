using System.Collections.ObjectModel;
using System.Globalization;
using Serilog;
using WPFLocalizeExtension.Engine;
using WPFLocalizeExtension.Providers;

namespace Boutique.Services;

public record LanguageOption(string Code, string DisplayName)
{
  public override string ToString() => DisplayName;
}

public class LocalizationService(ILogger logger, GuiSettingsService guiSettings)
{
  private readonly ILogger _logger = logger.ForContext<LocalizationService>();

  public ObservableCollection<LanguageOption> AvailableLanguages { get; } =
  [
    new("en", "English"),
    new("de", "Deutsch"),
    new("fr", "Français"),
    new("es", "Español"),
    new("pt-BR", "Português (Brasil)"),
    new("ru", "Русский"),
    new("zh-Hans", "简体中文"),
    new("ja", "日本語"),
    new("ko", "한국어")
  ];

  private static string CurrentLanguageCode
  {
    get
    {
      try
      {
        return LocalizeDictionary.Instance.Culture?.Name ?? "en";
      }
      catch
      {
        return "en";
      }
    }
  }

  public void Initialize()
  {
    try
    {
      ResxLocalizationProvider.Instance.FallbackAssembly = "Boutique";
      ResxLocalizationProvider.Instance.FallbackDictionary = "Strings";
    }
    catch (Exception ex)
    {
      _logger.Warning(ex, "Failed to set fallback assembly/dictionary for localization");
    }

    var savedLanguage = guiSettings.Language;
    if (!string.IsNullOrEmpty(savedLanguage))
    {
      SetLanguage(savedLanguage, false);
    }
    else
    {
      var systemCulture = CultureInfo.CurrentUICulture;
      var matchingLanguage = AvailableLanguages.FirstOrDefault(l =>
        l.Code.Equals(systemCulture.Name, StringComparison.OrdinalIgnoreCase) ||
        l.Code.Equals(systemCulture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));

      SetLanguage(matchingLanguage != null ? matchingLanguage.Code : "en");
    }

    _logger.Information("Localization initialized with language: {Language}", CurrentLanguageCode);
  }

  public void SetLanguage(string languageCode, bool save = true)
  {
    if (save)
    {
      guiSettings.Language = languageCode;
    }

    try
    {
      var culture = new CultureInfo(languageCode);
      LocalizeDictionary.Instance.Culture = culture;
      _logger.Information("Language changed to: {Language}", languageCode);
    }
    catch (CultureNotFoundException ex)
    {
      _logger.Warning(ex, "Failed to set language to {Language}, falling back to English", languageCode);
      try
      {
        LocalizeDictionary.Instance.Culture = new CultureInfo("en");
      }
      catch
      {
        // ignored
      }
    }
    catch (Exception ex)
    {
      _logger.Warning(ex, "Failed to set language to {Language}", languageCode);
    }
  }

  public LanguageOption? GetCurrentLanguageOption() =>
    AvailableLanguages.FirstOrDefault(l =>
      l.Code.Equals(CurrentLanguageCode, StringComparison.OrdinalIgnoreCase) ||
      CurrentLanguageCode.StartsWith(l.Code, StringComparison.OrdinalIgnoreCase));
}
