using Boutique.Utilities;
using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///     Tests for FormKeyHelper parsing utilities.
/// </summary>
public class FormKeyHelperTests
{
    #region TryParseModKey

    [Theory]
    [InlineData("Skyrim.esm", true)]
    [InlineData("MyMod.esp", true)]
    [InlineData("Light.esl", true)]
    [InlineData("SKYRIM.ESM", true)]
    [InlineData("NotAMod", false)]
    [InlineData("", false)]
    public void TryParseModKey_VariousInputs_ReturnsCorrectly(string input, bool expected)
    {
        var result = FormKeyHelper.TryParseModKey(input, out var modKey);
        result.Should().Be(expected);

        if (expected)
        {
            modKey.Should().NotBe(ModKey.Null);
        }
    }

    #endregion

    #region TryParse - FormKey parsing

    [Fact]
    public void TryParse_PipeFormat_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|0x12345", out var formKey);

        success.Should().BeTrue();
        formKey.ModKey.FileName.String.Should().Be("MyMod.esp");
        formKey.ID.Should().Be(0x12345u);
    }

    [Fact]
    public void TryParse_TildeFormat_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("0x12345~MyMod.esp", out var formKey);

        success.Should().BeTrue();
        formKey.ModKey.FileName.String.Should().Be("MyMod.esp");
        formKey.ID.Should().Be(0x12345u);
    }

    [Fact]
    public void TryParse_WithoutHexPrefix_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|12345", out var formKey);

        success.Should().BeTrue();
        formKey.ModKey.FileName.String.Should().Be("MyMod.esp");
        formKey.ID.Should().Be(0x12345u);
    }

    [Fact]
    public void TryParse_PlainEditorId_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse("SomeEditorId", out var formKey);

        success.Should().BeFalse();
        formKey.Should().Be(FormKey.Null);
    }

    [Fact]
    public void TryParse_InvalidModKey_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse("NotAMod|0x12345", out _);

        success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_EmptyString_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse("", out var formKey);

        success.Should().BeFalse();
        formKey.Should().Be(FormKey.Null);
    }

    [Fact]
    public void TryParse_NullString_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse(null!, out var formKey);

        success.Should().BeFalse();
        formKey.Should().Be(FormKey.Null);
    }

    [Theory]
    [InlineData("Skyrim.esm|0xABCDE")]
    [InlineData("Update.esm|0x1")]
    [InlineData("Dawnguard.esm|0x00FFFFFF")]
    [InlineData("MyMod.esl|0x800")]
    public void TryParse_VariousValidFormats_Succeeds(string input)
    {
        var success = FormKeyHelper.TryParse(input, out var formKey);
        success.Should().BeTrue();
        formKey.Should().NotBe(FormKey.Null);
    }

    [Fact]
    public void TryParse_PipeFormatWithSpaces_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse(" MyMod.esp | 0x12345 ", out var formKey);

        success.Should().BeTrue();
        formKey.ModKey.FileName.String.Should().Be("MyMod.esp");
        formKey.ID.Should().Be(0x12345u);
    }

    [Fact]
    public void TryParse_TildeFormatWithSpaces_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse(" 0x12345 ~ MyMod.esp ", out var formKey);

        success.Should().BeTrue();
        formKey.ModKey.FileName.String.Should().Be("MyMod.esp");
        formKey.ID.Should().Be(0x12345u);
    }

    [Fact]
    public void TryParse_UppercaseHex_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|0xABCDEF", out var formKey);

        success.Should().BeTrue();
        formKey.ID.Should().Be(0xABCDEFu);
    }

    [Fact]
    public void TryParse_LowercaseHex_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|0xabcdef", out var formKey);

        success.Should().BeTrue();
        formKey.ID.Should().Be(0xABCDEFu);
    }

    [Fact]
    public void TryParse_MixedCaseHex_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|0xAbCdEf", out var formKey);

        success.Should().BeTrue();
        formKey.ID.Should().Be(0xABCDEFu);
    }

    [Fact]
    public void TryParse_PaddedFormId_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|00012345", out var formKey);

        success.Should().BeTrue();
        formKey.ID.Should().Be(0x12345u);
    }

    [Fact]
    public void TryParse_EightDigitFormId_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|00ABCDEF", out var formKey);

        success.Should().BeTrue();
        formKey.ID.Should().Be(0xABCDEFu);
    }

    [Fact]
    public void TryParse_WhitespaceOnly_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse("   ", out var formKey);

        success.Should().BeFalse();
        formKey.Should().Be(FormKey.Null);
    }

    [Fact]
    public void TryParse_OnlyPipe_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse("|", out _);
        success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_OnlyTilde_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse("~", out _);
        success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_InvalidHexChars_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|0xGHIJKL", out _);
        success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_ModKeyWithSpaces_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("My Mod With Spaces.esp|0x800", out var formKey);

        success.Should().BeTrue();
        formKey.ModKey.FileName.String.Should().Be("My Mod With Spaces.esp");
    }

    [Theory]
    [InlineData("Skyrim.esm|0x7", "Skyrim.esm", 0x7u)]
    [InlineData("0x800~MyMod.esp", "MyMod.esp", 0x800u)]
    [InlineData("Test.esl|00ABCDEF", "Test.esl", 0xABCDEFu)]
    [InlineData("0xABCDEF~Another.esm", "Another.esm", 0xABCDEFu)]
    public void TryParse_BothFormats_ParsesCorrectly(string input, string expectedMod, uint expectedId)
    {
        var success = FormKeyHelper.TryParse(input, out var formKey);

        success.Should().BeTrue();
        formKey.ModKey.FileName.String.Should().Be(expectedMod);
        formKey.ID.Should().Be(expectedId);
    }

    #endregion

    #region Format - FormKey formatting

    [Fact]
    public void Format_StandardFormKey_FormatsCorrectly()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("MyMod.esp"), 0x12345);
        var result = FormKeyHelper.Format(formKey);

        result.Should().Be("MyMod.esp|00012345");
    }

    [Fact]
    public void Format_SmallFormId_PadsWithZeros()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("Test.esp"), 0x1);
        var result = FormKeyHelper.Format(formKey);

        result.Should().Be("Test.esp|00000001");
    }

    [Fact]
    public void Format_EslFile_FormatsCorrectly()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("Light.esl"), 0x800);
        var result = FormKeyHelper.Format(formKey);

        result.Should().Be("Light.esl|00000800");
    }

    [Fact]
    public void Format_EsmFile_FormatsCorrectly()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("Skyrim.esm"), 0xABCDEF);
        var result = FormKeyHelper.Format(formKey);

        result.Should().Be("Skyrim.esm|00ABCDEF");
    }

    [Fact]
    public void Format_LargeFormId_FormatsCorrectly()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("Test.esp"), 0x00FFFFFF);
        var result = FormKeyHelper.Format(formKey);

        result.Should().Be("Test.esp|00FFFFFF");
    }

    [Fact]
    public void Format_ZeroFormId_FormatsWithPadding()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("Test.esp"), 0x0);
        var result = FormKeyHelper.Format(formKey);

        result.Should().Be("Test.esp|00000000");
    }

    [Fact]
    public void Format_OutputCanBeParsedByTryParse()
    {
        var original = new FormKey(ModKey.FromNameAndExtension("MyMod.esp"), 0x12345);
        var formatted = FormKeyHelper.Format(original);
        var success = FormKeyHelper.TryParse(formatted, out var parsed);

        success.Should().BeTrue();
        parsed.Should().Be(original);
    }

    [Theory]
    [InlineData("Mod.esp", 0x800u, "Mod.esp|00000800")]
    [InlineData("Skyrim.esm", 0x7u, "Skyrim.esm|00000007")]
    [InlineData("Test.esl", 0x00ABCDEF, "Test.esl|00ABCDEF")]
    [InlineData("Space In Name.esp", 0x100u, "Space In Name.esp|00000100")]
    public void Format_VariousInputs_FormatsCorrectly(string modName, uint formId, string expected)
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension(modName), formId);
        var result = FormKeyHelper.Format(formKey);

        result.Should().Be(expected);
    }

    #endregion

    #region FormatForSpid - SPID syntax formatting

    [Fact]
    public void FormatForSpid_StandardFormKey_FormatsWithTilde()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("MyMod.esp"), 0x12345);
        var result = FormKeyHelper.FormatForSpid(formKey);

        result.Should().Be("0x12345~MyMod.esp");
    }

    [Fact]
    public void FormatForSpid_SmallFormId_NoLeadingZeros()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("Test.esp"), 0x800);
        var result = FormKeyHelper.FormatForSpid(formKey);

        result.Should().Be("0x800~Test.esp");
    }

    [Fact]
    public void FormatForSpid_LargeFormId_FormatsCorrectly()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("Test.esp"), 0xABCDEF);
        var result = FormKeyHelper.FormatForSpid(formKey);

        result.Should().Be("0xABCDEF~Test.esp");
    }

    [Fact]
    public void FormatForSpid_OutputCanBeParsedByTryParse()
    {
        var original = new FormKey(ModKey.FromNameAndExtension("MyMod.esp"), 0x800);
        var formatted = FormKeyHelper.FormatForSpid(original);
        var success = FormKeyHelper.TryParse(formatted, out var parsed);

        success.Should().BeTrue();
        parsed.Should().Be(original);
    }

    [Theory]
    [InlineData("Mod.esp", 0x1u, "0x1~Mod.esp")]
    [InlineData("Skyrim.esm", 0x7u, "0x7~Skyrim.esm")]
    [InlineData("Test.esl", 0xABCDEFu, "0xABCDEF~Test.esl")]
    public void FormatForSpid_VariousInputs_FormatsCorrectly(string modName, uint formId, string expected)
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension(modName), formId);
        var result = FormKeyHelper.FormatForSpid(formKey);

        result.Should().Be(expected);
    }

    #endregion

    #region TryParseFormId

    [Theory]
    [InlineData("0x12345", 0x12345u)]
    [InlineData("0X12345", 0x12345u)]
    [InlineData("12345", 0x12345u)]
    [InlineData("ABCDEF", 0xABCDEFu)]
    [InlineData("1", 0x1u)]
    public void TryParseFormId_ValidInputs_ParsesCorrectly(string input, uint expected)
    {
        var success = FormKeyHelper.TryParseFormId(input, out var id);

        success.Should().BeTrue();
        id.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotHex")]
    [InlineData("GHIJKL")]
    public void TryParseFormId_InvalidInputs_ReturnsFalse(string input)
    {
        var success = FormKeyHelper.TryParseFormId(input, out _);
        success.Should().BeFalse();
    }

    #endregion

    #region TryParseEditorIdReference

    [Fact]
    public void TryParseEditorIdReference_PlainEditorId_ReturnsEditorIdNoMod()
    {
        var success = FormKeyHelper.TryParseEditorIdReference("MyEditorId", out var modKey, out var editorId);

        success.Should().BeTrue();
        modKey.Should().BeNull();
        editorId.Should().Be("MyEditorId");
    }

    [Fact]
    public void TryParseEditorIdReference_EditorIdWithModPipe_ParsesBoth()
    {
        var success = FormKeyHelper.TryParseEditorIdReference("MyEditorId|MyMod.esp", out var modKey, out var editorId);

        success.Should().BeTrue();
        modKey.Should().NotBeNull();
        modKey!.Value.FileName.String.Should().Be("MyMod.esp");
        editorId.Should().Be("MyEditorId");
    }

    [Fact]
    public void TryParseEditorIdReference_ModPipeEditorId_ParsesBoth()
    {
        var success = FormKeyHelper.TryParseEditorIdReference("MyMod.esp|MyEditorId", out var modKey, out var editorId);

        success.Should().BeTrue();
        modKey.Should().NotBeNull();
        modKey!.Value.FileName.String.Should().Be("MyMod.esp");
        editorId.Should().Be("MyEditorId");
    }

    [Fact]
    public void TryParseEditorIdReference_EditorIdWithModTilde_ParsesBoth()
    {
        var success = FormKeyHelper.TryParseEditorIdReference("MyEditorId~MyMod.esp", out var modKey, out var editorId);

        success.Should().BeTrue();
        modKey.Should().NotBeNull();
        modKey!.Value.FileName.String.Should().Be("MyMod.esp");
        editorId.Should().Be("MyEditorId");
    }

    [Fact]
    public void TryParseEditorIdReference_EmptyString_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParseEditorIdReference("", out var modKey, out var editorId);

        success.Should().BeFalse();
        modKey.Should().BeNull();
        editorId.Should().BeEmpty();
    }

    #endregion
}
