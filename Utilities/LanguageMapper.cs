using Mutagen.Bethesda.Strings;

namespace Boutique.Utilities;

/// <summary>
/// Maps UI language codes to Mutagen's Language enum for STRINGS file loading.
/// </summary>
public static class LanguageMapper
{
  /// <summary>
  /// Converts a language code (e.g., "en", "zh-Hans", "zh") to Mutagen's Language enum.
  /// Supports both simple codes (en, zh) and culture-specific codes (zh-Hans, zh-Hant).
  /// </summary>
  public static Language ToMutagenLanguage(string languageCode)
  {
    var lowerCode = languageCode.ToLowerInvariant();

    // Extract the primary language code (before any dash)
    var primaryCode = lowerCode.Split('-')[0];

    return primaryCode switch
    {
      "en" => Language.English,
      "zh" => Language.Chinese,
      "fr" => Language.French,
      "de" => Language.German,
      "it" => Language.Italian,
      "ja" => Language.Japanese,
      "pl" => Language.Polish,
      "ru" => Language.Russian,
      "es" => Language.Spanish,
      _    => Language.English // Default fallback
    };
  }
}
