using System.Text.RegularExpressions;

namespace Boutique.Utilities;

/// <summary>
///   Centralized regex patterns for input validation and sanitization.
/// </summary>
public static partial class InputPatterns
{
  // Identifier: alphanumeric + underscore
  [GeneratedRegex("^[A-Za-z0-9_]+$")]
  private static partial Regex IdentifierValidatorRegex();

  [GeneratedRegex("[^A-Za-z0-9_]")]
  private static partial Regex IdentifierSanitizerRegex();

  /// <summary>
  ///   Identifier pattern: letters (A-Z, a-z), digits (0-9), and underscores.
  ///   Valid for EditorIDs, variable names, and similar identifiers.
  /// </summary>
  public static class Identifier
  {
    /// <summary>
    ///   Validates that a string contains only identifier characters.
    /// </summary>
    public static bool IsValid(string? value) =>
      !string.IsNullOrEmpty(value) && IdentifierValidatorRegex().IsMatch(value);

    /// <summary>
    ///   Sanitizes a string by removing non-identifier characters.
    /// </summary>
    public static string Sanitize(string? value) =>
      string.IsNullOrEmpty(value) ? string.Empty : IdentifierSanitizerRegex().Replace(value, string.Empty);

    /// <summary>
    ///   Sanitizes a string, returning a fallback if the result is empty.
    /// </summary>
    public static string SanitizeOrDefault(string? value, string fallback = "Unnamed") =>
      Sanitize(value) is { Length: > 0 } sanitized ? sanitized : fallback;
  }
}
