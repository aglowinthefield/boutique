using System.Text;
using Xunit;

namespace Boutique.Tests;

public class EncodingTests
{
    [Fact]
    public void CodePagesEncodingProvider_Cp1251_IsAvailable()
    {
        // Register the provider (same as App.xaml.cs does at startup)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // This would throw if CP1251 isn't available
        var cp1251 = Encoding.GetEncoding(1251);

        Assert.NotNull(cp1251);
        Assert.Equal("windows-1251", cp1251.WebName);
    }

    [Fact]
    public void CodePagesEncodingProvider_Cp1251_CanDecodeRussianText()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1251 = Encoding.GetEncoding(1251);

        // Russian text "Броня" (Armor)
        var originalText = "Броня";

        // Encode to CP1251 bytes, then decode back
        var bytes = cp1251.GetBytes(originalText);
        var decoded = cp1251.GetString(bytes);

        Assert.Equal(originalText, decoded);
    }

    [Fact]
    public void CodePagesEncodingProvider_Cp1251_RoundTripCyrillicCharacters()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1251 = Encoding.GetEncoding(1251);

        // Various Russian armor-related words
        var testStrings = new[]
        {
            "Стальной шлем",      // Steel Helmet
            "Кожаные сапоги",     // Leather Boots
            "Железная кираса",    // Iron Cuirass
            "Эбонитовые перчатки" // Ebony Gauntlets
        };

        foreach (var original in testStrings)
        {
            var bytes = cp1251.GetBytes(original);
            var decoded = cp1251.GetString(bytes);
            Assert.Equal(original, decoded);
        }
    }

    [Fact]
    public void CodePagesEncodingProvider_Cp1251_BytesDecodeCorrectly()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1251 = Encoding.GetEncoding(1251);

        // Known CP1251 bytes for "Броня" (Armor)
        // Б=193, р=240, о=238, н=237, я=255
        var knownBytes = new byte[] { 193, 240, 238, 237, 255 };
        var decoded = cp1251.GetString(knownBytes);

        Assert.Equal("Броня", decoded);
    }
}
