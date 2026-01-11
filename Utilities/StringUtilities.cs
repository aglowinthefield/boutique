namespace Boutique.Utilities;

public static class StringUtilities
{
    private static readonly char[] CommentChars = [';', '#'];

    public static string RemoveInlineComment(string text)
    {
        var commentIndex = text.IndexOfAny(CommentChars);
        if (commentIndex >= 0)
            text = text[..commentIndex];

        return text.Trim();
    }
}
