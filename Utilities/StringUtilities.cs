using Noggog;

namespace Boutique.Utilities;

public static class StringUtilities
{
  private static readonly char[] _commentChars = [';', '#'];

  public static string RemoveInlineComment(string text)
  {
    var commentIndex = text.IndexOfAny(_commentChars);
    if (commentIndex >= 0)
    {
      text = text[..commentIndex];
    }

    return text.Trim();
  }

  public static bool AnyContainValue(IReadOnlySet<string>? strings, string value) =>
    strings?.Any(s => ContainsValue(s, value)) ?? false;

  public static bool ContainsValue(string? source, string value) =>
    !source.IsNullOrWhitespace() && source.Contains(value, StringComparison.OrdinalIgnoreCase);
}
