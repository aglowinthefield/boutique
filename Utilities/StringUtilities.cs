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
}
