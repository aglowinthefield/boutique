using System.Text;
using FluentAssertions;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///     Tests for file encoding detection and handling.
/// </summary>
public class EncodingTests
{
    [Fact]
    public void Utf8Bom_IsDetected()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var content = Encoding.UTF8.GetBytes("test content");
        var withBom = bom.Concat(content).ToArray();

        var encoding = DetectEncoding(withBom);

        encoding.Should().Be(Encoding.UTF8);
    }

    [Fact]
    public void Utf16LeBom_IsDetected()
    {
        var bom = new byte[] { 0xFF, 0xFE };
        var content = Encoding.Unicode.GetBytes("test");
        var withBom = bom.Concat(content).ToArray();

        var encoding = DetectEncoding(withBom);

        encoding.Should().Be(Encoding.Unicode);
    }

    [Fact]
    public void Utf16BeBom_IsDetected()
    {
        var bom = new byte[] { 0xFE, 0xFF };
        var content = Encoding.BigEndianUnicode.GetBytes("test");
        var withBom = bom.Concat(content).ToArray();

        var encoding = DetectEncoding(withBom);

        encoding.Should().Be(Encoding.BigEndianUnicode);
    }

    [Fact]
    public void NoBom_DefaultsToUtf8()
    {
        var content = Encoding.UTF8.GetBytes("plain ascii content");

        var encoding = DetectEncoding(content);

        encoding.Should().Be(Encoding.UTF8);
    }

    [Fact]
    public void EmptyBytes_DefaultsToUtf8()
    {
        var empty = Array.Empty<byte>();

        var encoding = DetectEncoding(empty);

        encoding.Should().Be(Encoding.UTF8);
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        return Encoding.UTF8;
    }
}
