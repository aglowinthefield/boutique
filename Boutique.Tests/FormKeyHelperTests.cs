using Boutique.Utilities;
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
        Assert.Equal(expected, result);

        if (expected) Assert.NotEqual(ModKey.Null, modKey);
    }

    #endregion

    #region TryParse - FormKey parsing

    [Fact]
    public void TryParse_PipeFormat_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|0x12345", out var formKey);

        Assert.True(success);
        Assert.Equal("MyMod.esp", formKey.ModKey.FileName);
        Assert.Equal(0x12345u, formKey.ID);
    }

    [Fact]
    public void TryParse_TildeFormat_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("0x12345~MyMod.esp", out var formKey);

        Assert.True(success);
        Assert.Equal("MyMod.esp", formKey.ModKey.FileName);
        Assert.Equal(0x12345u, formKey.ID);
    }

    [Fact]
    public void TryParse_WithoutHexPrefix_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|12345", out var formKey);

        Assert.True(success);
        Assert.Equal("MyMod.esp", formKey.ModKey.FileName);
        Assert.Equal(0x12345u, formKey.ID);
    }

    [Fact]
    public void TryParse_PlainEditorId_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse("SomeEditorId", out var formKey);

        Assert.False(success);
        Assert.Equal(FormKey.Null, formKey);
    }

    [Fact]
    public void TryParse_InvalidModKey_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse("NotAMod|0x12345", out _);

        Assert.False(success);
    }

    [Fact]
    public void TryParse_EmptyString_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse("", out var formKey);

        Assert.False(success);
        Assert.Equal(FormKey.Null, formKey);
    }

    [Fact]
    public void TryParse_NullString_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse(null!, out var formKey);

        Assert.False(success);
        Assert.Equal(FormKey.Null, formKey);
    }

    [Theory]
    [InlineData("Skyrim.esm|0xABCDE")]
    [InlineData("Update.esm|0x1")]
    [InlineData("Dawnguard.esm|0x00FFFFFF")]
    [InlineData("MyMod.esl|0x800")]
    public void TryParse_VariousValidFormats_Succeeds(string input)
    {
        var success = FormKeyHelper.TryParse(input, out var formKey);
        Assert.True(success);
        Assert.NotEqual(FormKey.Null, formKey);
    }

    [Fact]
    public void TryParse_PipeFormatWithSpaces_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse(" MyMod.esp | 0x12345 ", out var formKey);

        Assert.True(success);
        Assert.Equal("MyMod.esp", formKey.ModKey.FileName);
        Assert.Equal(0x12345u, formKey.ID);
    }

    [Fact]
    public void TryParse_TildeFormatWithSpaces_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse(" 0x12345 ~ MyMod.esp ", out var formKey);

        Assert.True(success);
        Assert.Equal("MyMod.esp", formKey.ModKey.FileName);
        Assert.Equal(0x12345u, formKey.ID);
    }

    [Fact]
    public void TryParse_UppercaseHex_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|0xABCDEF", out var formKey);

        Assert.True(success);
        Assert.Equal(0xABCDEFu, formKey.ID);
    }

    [Fact]
    public void TryParse_LowercaseHex_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|0xabcdef", out var formKey);

        Assert.True(success);
        Assert.Equal(0xABCDEFu, formKey.ID);
    }

    [Fact]
    public void TryParse_MixedCaseHex_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|0xAbCdEf", out var formKey);

        Assert.True(success);
        Assert.Equal(0xABCDEFu, formKey.ID);
    }

    [Fact]
    public void TryParse_PaddedFormId_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|00012345", out var formKey);

        Assert.True(success);
        Assert.Equal(0x12345u, formKey.ID);
    }

    [Fact]
    public void TryParse_EightDigitFormId_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|00ABCDEF", out var formKey);

        Assert.True(success);
        Assert.Equal(0xABCDEFu, formKey.ID);
    }

    [Fact]
    public void TryParse_WhitespaceOnly_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse("   ", out var formKey);

        Assert.False(success);
        Assert.Equal(FormKey.Null, formKey);
    }

    [Fact]
    public void TryParse_OnlyPipe_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse("|", out _);

        Assert.False(success);
    }

    [Fact]
    public void TryParse_OnlyTilde_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse("~", out _);

        Assert.False(success);
    }

    [Fact]
    public void TryParse_InvalidHexChars_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParse("MyMod.esp|0xGHIJKL", out _);

        Assert.False(success);
    }

    [Fact]
    public void TryParse_ModKeyWithSpaces_ParsesCorrectly()
    {
        var success = FormKeyHelper.TryParse("My Mod With Spaces.esp|0x800", out var formKey);

        Assert.True(success);
        Assert.Equal("My Mod With Spaces.esp", formKey.ModKey.FileName);
    }

    [Theory]
    [InlineData("Skyrim.esm|0x7", "Skyrim.esm", 0x7u)]
    [InlineData("0x800~MyMod.esp", "MyMod.esp", 0x800u)]
    [InlineData("Test.esl|00ABCDEF", "Test.esl", 0xABCDEFu)]
    [InlineData("0xABCDEF~Another.esm", "Another.esm", 0xABCDEFu)]
    public void TryParse_BothFormats_ParsesCorrectly(string input, string expectedMod, uint expectedId)
    {
        var success = FormKeyHelper.TryParse(input, out var formKey);

        Assert.True(success);
        Assert.Equal(expectedMod, formKey.ModKey.FileName);
        Assert.Equal(expectedId, formKey.ID);
    }

    #endregion

    #region Format - FormKey formatting

    [Fact]
    public void Format_StandardFormKey_FormatsCorrectly()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("MyMod.esp"), 0x12345);
        var result = FormKeyHelper.Format(formKey);

        Assert.Equal("MyMod.esp|00012345", result);
    }

    [Fact]
    public void Format_SmallFormId_PadsWithZeros()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("Test.esp"), 0x1);
        var result = FormKeyHelper.Format(formKey);

        Assert.Equal("Test.esp|00000001", result);
    }

    [Fact]
    public void Format_EslFile_FormatsCorrectly()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("Light.esl"), 0x800);
        var result = FormKeyHelper.Format(formKey);

        Assert.Equal("Light.esl|00000800", result);
    }

    [Fact]
    public void Format_EsmFile_FormatsCorrectly()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("Skyrim.esm"), 0xABCDEF);
        var result = FormKeyHelper.Format(formKey);

        Assert.Equal("Skyrim.esm|00ABCDEF", result);
    }

    [Fact]
    public void Format_LargeFormId_FormatsCorrectly()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("Test.esp"), 0x00FFFFFF);
        var result = FormKeyHelper.Format(formKey);

        Assert.Equal("Test.esp|00FFFFFF", result);
    }

    [Fact]
    public void Format_ZeroFormId_FormatsWithPadding()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("Test.esp"), 0x0);
        var result = FormKeyHelper.Format(formKey);

        Assert.Equal("Test.esp|00000000", result);
    }

    [Fact]
    public void Format_OutputCanBeParsedByTryParse()
    {
        var original = new FormKey(ModKey.FromNameAndExtension("MyMod.esp"), 0x12345);
        var formatted = FormKeyHelper.Format(original);
        var success = FormKeyHelper.TryParse(formatted, out var parsed);

        Assert.True(success);
        Assert.Equal(original, parsed);
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

        Assert.Equal(expected, result);
    }

    #endregion

    #region FormatForSpid - SPID syntax formatting

    [Fact]
    public void FormatForSpid_StandardFormKey_FormatsWithTilde()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("MyMod.esp"), 0x12345);
        var result = FormKeyHelper.FormatForSpid(formKey);

        Assert.Equal("0x12345~MyMod.esp", result);
    }

    [Fact]
    public void FormatForSpid_SmallFormId_NoLeadingZeros()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("Test.esp"), 0x800);
        var result = FormKeyHelper.FormatForSpid(formKey);

        Assert.Equal("0x800~Test.esp", result);
    }

    [Fact]
    public void FormatForSpid_LargeFormId_FormatsCorrectly()
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension("Test.esp"), 0xABCDEF);
        var result = FormKeyHelper.FormatForSpid(formKey);

        Assert.Equal("0xABCDEF~Test.esp", result);
    }

    [Fact]
    public void FormatForSpid_OutputCanBeParsedByTryParse()
    {
        var original = new FormKey(ModKey.FromNameAndExtension("MyMod.esp"), 0x800);
        var formatted = FormKeyHelper.FormatForSpid(original);
        var success = FormKeyHelper.TryParse(formatted, out var parsed);

        Assert.True(success);
        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData("Mod.esp", 0x1u, "0x1~Mod.esp")]
    [InlineData("Skyrim.esm", 0x7u, "0x7~Skyrim.esm")]
    [InlineData("Test.esl", 0xABCDEFu, "0xABCDEF~Test.esl")]
    public void FormatForSpid_VariousInputs_FormatsCorrectly(string modName, uint formId, string expected)
    {
        var formKey = new FormKey(ModKey.FromNameAndExtension(modName), formId);
        var result = FormKeyHelper.FormatForSpid(formKey);

        Assert.Equal(expected, result);
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

        Assert.True(success);
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotHex")]
    [InlineData("GHIJKL")]
    public void TryParseFormId_InvalidInputs_ReturnsFalse(string input)
    {
        var success = FormKeyHelper.TryParseFormId(input, out _);
        Assert.False(success);
    }

    #endregion

    #region TryParseEditorIdReference

    [Fact]
    public void TryParseEditorIdReference_PlainEditorId_ReturnsEditorIdNoMod()
    {
        var success = FormKeyHelper.TryParseEditorIdReference("MyEditorId", out var modKey, out var editorId);

        Assert.True(success);
        Assert.Null(modKey);
        Assert.Equal("MyEditorId", editorId);
    }

    [Fact]
    public void TryParseEditorIdReference_EditorIdWithModPipe_ParsesBoth()
    {
        var success = FormKeyHelper.TryParseEditorIdReference("MyEditorId|MyMod.esp", out var modKey, out var editorId);

        Assert.True(success);
        Assert.NotNull(modKey);
        Assert.Equal("MyMod.esp", modKey.Value.FileName);
        Assert.Equal("MyEditorId", editorId);
    }

    [Fact]
    public void TryParseEditorIdReference_ModPipeEditorId_ParsesBoth()
    {
        var success = FormKeyHelper.TryParseEditorIdReference("MyMod.esp|MyEditorId", out var modKey, out var editorId);

        Assert.True(success);
        Assert.NotNull(modKey);
        Assert.Equal("MyMod.esp", modKey.Value.FileName);
        Assert.Equal("MyEditorId", editorId);
    }

    [Fact]
    public void TryParseEditorIdReference_EditorIdWithModTilde_ParsesBoth()
    {
        var success = FormKeyHelper.TryParseEditorIdReference("MyEditorId~MyMod.esp", out var modKey, out var editorId);

        Assert.True(success);
        Assert.NotNull(modKey);
        Assert.Equal("MyMod.esp", modKey.Value.FileName);
        Assert.Equal("MyEditorId", editorId);
    }

    [Fact]
    public void TryParseEditorIdReference_EmptyString_ReturnsFalse()
    {
        var success = FormKeyHelper.TryParseEditorIdReference("", out var modKey, out var editorId);

        Assert.False(success);
        Assert.Null(modKey);
        Assert.Equal(string.Empty, editorId);
    }

    #endregion
}
