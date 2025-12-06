using Boutique.Utilities;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace Boutique.Tests;

/// <summary>
/// Tests for FormKeyHelper parsing utilities.
/// </summary>
public class FormKeyHelperTests
{
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
        var success = FormKeyHelper.TryParse("NotAMod|0x12345", out var formKey);
        
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

    #endregion

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
        
        if (expected)
        {
            Assert.NotEqual(ModKey.Null, modKey);
        }
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

