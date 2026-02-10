using Boutique.Models;
using Boutique.Services;
using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace Boutique.Tests;

public class SpidFilterMatchingServiceTests
{
  #region PartialMatchesNpcStrings

  [Fact]
  public void PartialMatchesNpcStrings_MatchByName_ReturnsTrue()
  {
    var npc = CreateTestNpc(name: "Whiterun Guard");
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Guard", null);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_MatchByNameCaseInsensitive_ReturnsTrue()
  {
    var npc = CreateTestNpc(name: "Whiterun Guard");
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "guard", null);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_MatchByNamePartial_ReturnsTrue()
  {
    var npc = CreateTestNpc(name: "Whiterun Guard");
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "run", null);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_NullName_ReturnsFalse()
  {
    var npc = CreateTestNpc(name: null);
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Guard", null);
    result.Should().BeFalse();
  }

  [Fact]
  public void PartialMatchesNpcStrings_EmptyName_ReturnsFalse()
  {
    var npc = CreateTestNpc(name: "");
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Guard", null);
    result.Should().BeFalse();
  }

  [Fact]
  public void PartialMatchesNpcStrings_WhitespaceName_ReturnsFalse()
  {
    var npc = CreateTestNpc(name: "   ");
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Guard", null);
    result.Should().BeFalse();
  }

  [Fact]
  public void PartialMatchesNpcStrings_MatchByEditorId_ReturnsTrue()
  {
    var npc = CreateTestNpc(editorId: "EncBandit01");
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Bandit", null);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_MatchByEditorIdCaseInsensitive_ReturnsTrue()
  {
    var npc = CreateTestNpc(editorId: "EncBandit01");
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "bandit", null);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_NullEditorId_ReturnsFalse()
  {
    var npc = CreateTestNpc(editorId: null);
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Bandit", null);
    result.Should().BeFalse();
  }

  [Fact]
  public void PartialMatchesNpcStrings_EmptyEditorId_ReturnsFalse()
  {
    var npc = CreateTestNpc(editorId: "");
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Bandit", null);
    result.Should().BeFalse();
  }

  [Fact]
  public void PartialMatchesNpcStrings_MatchByKeyword_ReturnsTrue()
  {
    var npc = CreateTestNpc(keywords: new HashSet<string> { "ActorTypeNPC", "ActorTypeHuman" });
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Human", null);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_MatchByKeywordCaseInsensitive_ReturnsTrue()
  {
    var npc = CreateTestNpc(keywords: new HashSet<string> { "ActorTypeNPC", "ActorTypeHuman" });
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "human", null);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_MatchByKeywordPartial_ReturnsTrue()
  {
    var npc = CreateTestNpc(keywords: new HashSet<string> { "ActorTypeNPC", "ActorTypeHuman" });
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Type", null);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_EmptyKeywords_ReturnsFalse()
  {
    var npc = CreateTestNpc(keywords: new HashSet<string>());
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Human", null);
    result.Should().BeFalse();
  }

  [Fact]
  public void PartialMatchesNpcStrings_MatchByVirtualKeyword_ReturnsTrue()
  {
    var npc = CreateTestNpc();
    var virtualKeywords = new HashSet<string> { "CustomKeyword1", "CustomKeyword2" };
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Keyword1", virtualKeywords);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_MatchByVirtualKeywordCaseInsensitive_ReturnsTrue()
  {
    var npc = CreateTestNpc();
    var virtualKeywords = new HashSet<string> { "CustomKeyword1", "CustomKeyword2" };
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "keyword1", virtualKeywords);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_NullVirtualKeywords_ReturnsFalse()
  {
    var npc = CreateTestNpc();
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "CustomKeyword", null);
    result.Should().BeFalse();
  }

  [Fact]
  public void PartialMatchesNpcStrings_EmptyVirtualKeywords_ReturnsFalse()
  {
    var npc = CreateTestNpc();
    var virtualKeywords = new HashSet<string>();
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "CustomKeyword", virtualKeywords);
    result.Should().BeFalse();
  }

  [Fact]
  public void PartialMatchesNpcStrings_MatchByTemplateEditorId_ReturnsTrue()
  {
    var npc = CreateTestNpc(templateEditorId: "LvlBanditTemplate");
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Template", null);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_MatchByTemplateEditorIdCaseInsensitive_ReturnsTrue()
  {
    var npc = CreateTestNpc(templateEditorId: "LvlBanditTemplate");
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "template", null);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_NullTemplateEditorId_ReturnsFalse()
  {
    var npc = CreateTestNpc(templateEditorId: null);
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Template", null);
    result.Should().BeFalse();
  }

  [Fact]
  public void PartialMatchesNpcStrings_EmptyTemplateEditorId_ReturnsFalse()
  {
    var npc = CreateTestNpc(templateEditorId: "");
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Template", null);
    result.Should().BeFalse();
  }

  [Fact]
  public void PartialMatchesNpcStrings_NoMatchesAnywhere_ReturnsFalse()
  {
    var npc = CreateTestNpc(
      name: "Lydia",
      editorId: "Housecarl",
      keywords: new HashSet<string> { "ActorTypeNPC" },
      templateEditorId: null);
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Dragon", null);
    result.Should().BeFalse();
  }

  [Fact]
  public void PartialMatchesNpcStrings_MultipleMatches_ReturnsTrue()
  {
    var npc = CreateTestNpc(
      name: "Guard",
      editorId: "WhiterunGuard",
      keywords: new HashSet<string> { "GuardKeyword" });
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Guard", null);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_MatchInDifferentFields_ReturnsTrue()
  {
    var npc1 = CreateTestNpc(name: "Bandit");
    SpidFilterMatchingService.PartialMatchesNpcStrings(npc1, "Bandit", null).Should().BeTrue();

    var npc2 = CreateTestNpc(editorId: "Bandit");
    SpidFilterMatchingService.PartialMatchesNpcStrings(npc2, "Bandit", null).Should().BeTrue();

    var npc3 = CreateTestNpc(keywords: new HashSet<string> { "Bandit" });
    SpidFilterMatchingService.PartialMatchesNpcStrings(npc3, "Bandit", null).Should().BeTrue();

    var npc4 = CreateTestNpc(templateEditorId: "Bandit");
    SpidFilterMatchingService.PartialMatchesNpcStrings(npc4, "Bandit", null).Should().BeTrue();
  }

  [Theory]
  [InlineData("GUARD", "guard")]
  [InlineData("Guard", "GUARD")]
  [InlineData("GuArD", "gUaRd")]
  public void PartialMatchesNpcStrings_CaseInsensitiveMatching_ReturnsTrue(
    string npcValue,
    string searchValue)
  {
    var npc = CreateTestNpc(name: npcValue);
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, searchValue, null);
    result.Should().BeTrue();
  }

  [Theory]
  [InlineData("Whiterun Guard", "Guard")]
  [InlineData("Whiterun Guard", "run")]
  [InlineData("Whiterun Guard", "White")]
  [InlineData("Whiterun Guard", "Whiterun Guard")]
  public void PartialMatchesNpcStrings_PartialMatchVariations_ReturnsTrue(
    string npcName,
    string searchValue)
  {
    var npc = CreateTestNpc(name: npcName);
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, searchValue, null);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_CombinedKeywordsAndVirtualKeywords_ReturnsTrue()
  {
    var npc = CreateTestNpc(keywords: new HashSet<string> { "Keyword1", "Keyword2" });
    var virtualKeywords = new HashSet<string> { "VirtualKeyword1", "VirtualKeyword2" };

    SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Keyword1", virtualKeywords).Should().BeTrue();
    SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "VirtualKeyword1", virtualKeywords).Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_AllFieldsNull_ReturnsFalse()
  {
    var npc = CreateTestNpc(
      name: null,
      editorId: null,
      keywords: new HashSet<string>(),
      templateEditorId: null);
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Anything", null);
    result.Should().BeFalse();
  }

  [Fact]
  public void PartialMatchesNpcStrings_EmptySearchValue_ReturnsTrue()
  {
    var npc = CreateTestNpc(name: "Guard");
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "", null);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_WhitespaceSearchValue_ReturnsFalse()
  {
    var npc = CreateTestNpc(name: "Guard");
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "   ", null);
    result.Should().BeFalse();
  }

  [Fact]
  public void PartialMatchesNpcStrings_SpecialCharacters_MatchesCorrectly()
  {
    var npc = CreateTestNpc(name: "Guard (Whiterun)");
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "(Whiterun)", null);
    result.Should().BeTrue();
  }

  [Fact]
  public void PartialMatchesNpcStrings_UnicodeCharacters_MatchesCorrectly()
  {
    var npc = CreateTestNpc(name: "Jarl Balgruuf");
    var result = SpidFilterMatchingService.PartialMatchesNpcStrings(npc, "Balgruuf", null);
    result.Should().BeTrue();
  }

  #endregion

  #region Helper Methods

  private static NpcFilterData CreateTestNpc(
    string? name = "TestNpc",
    string? editorId = "TestEditorId",
    IReadOnlySet<string>? keywords = null,
    string? templateEditorId = null)
  {
    var modKey = ModKey.FromNameAndExtension("Skyrim.esm");
    var formKey = new FormKey(modKey, 0x12345);

    return new NpcFilterData
    {
      FormKey = formKey,
      EditorId = editorId,
      Name = name,
      SourceMod = modKey,
      Keywords = keywords ?? new HashSet<string>(),
      Factions = new List<FactionMembership>(),
      RaceFormKey = null,
      RaceEditorId = null,
      ClassFormKey = null,
      ClassEditorId = null,
      CombatStyleFormKey = null,
      CombatStyleEditorId = null,
      VoiceTypeFormKey = null,
      VoiceTypeEditorId = null,
      DefaultOutfitFormKey = null,
      DefaultOutfitEditorId = null,
      WornArmorFormKey = null,
      IsFemale = false,
      IsUnique = false,
      IsSummonable = false,
      IsChild = false,
      IsLeveled = false,
      Level = 1,
      TemplateFormKey = null,
      TemplateEditorId = templateEditorId,
      SkillValues = new byte[24]
    };
  }

  #endregion
}
