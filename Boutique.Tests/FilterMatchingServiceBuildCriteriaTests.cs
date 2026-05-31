using Boutique.Models;
using Boutique.Services;
using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///   Characterization tests pinning the exact behavior of BuildMatchCriteria (reached via
///   GetMatchingNpcsForEntry) before refactoring it for cognitive complexity. Each test asserts the
///   precise criteria string assembled for a matching NPC, or that a non-matching NPC is filtered out.
/// </summary>
public class FilterMatchingServiceBuildCriteriaTests
{
  private static readonly ModKey TestMod = ModKey.FromNameAndExtension("Skyrim.esm");

  private static FormKey Fk(uint id) => new(TestMod, id);

  private static string? Criteria(
    NpcFilterData npc,
    DistributionEntry entry,
    IReadOnlyDictionary<FormKey, HashSet<string>>? virtualKeywords = null)
  {
    var results = FilterMatchingService.GetMatchingNpcsForEntry([npc], entry, virtualKeywords);
    return results.Count == 0 ? null : results[0].Criteria;
  }

  [Fact]
  public void NoFilters_ReturnsAllNpcs()
  {
    Criteria(new NpcBuilder().Build(), new DistributionEntry()).Should().Be("All NPCs");
  }

  [Fact]
  public void RawStringWildcard_Match_AddsStringPart()
  {
    var npc = new NpcBuilder { Name = "Whiterun Guard" }.Build();
    var entry = new DistributionEntry { RawStringFilters = "*Guard" };
    Criteria(npc, entry).Should().Be("String: *Guard");
  }

  [Fact]
  public void RawStringWildcard_NoMatch_ReturnsNull()
  {
    var npc = new NpcBuilder { Name = "Bandit" }.Build();
    var entry = new DistributionEntry { RawStringFilters = "*Guard" };
    Criteria(npc, entry).Should().BeNull();
  }

  [Fact]
  public void RawStringExact_MatchViaMatchKeys_AddsStringPart()
  {
    var npc = new NpcBuilder { MatchKeys = new HashSet<string> { "ActorTypeNPC" } }.Build();
    var entry = new DistributionEntry { RawStringFilters = "ActorTypeNPC" };
    Criteria(npc, entry).Should().Be("String: ActorTypeNPC");
  }

  [Fact]
  public void NpcFilter_Included_AddsNpcPartWithDisplayName()
  {
    var npc = new NpcBuilder { Name = "Lydia" }.Build();
    var entry = new DistributionEntry { NpcFilters = [new FormKeyFilter(npc.FormKey)] };
    Criteria(npc, entry).Should().Be("NPC: Lydia");
  }

  [Fact]
  public void NpcFilter_ExcludedMatching_ReturnsNull()
  {
    var npc = new NpcBuilder().Build();
    var entry = new DistributionEntry { NpcFilters = [new FormKeyFilter(npc.FormKey, IsExcluded: true)] };
    Criteria(npc, entry).Should().BeNull();
  }

  [Fact]
  public void NpcFilter_IncludedNonMatching_ReturnsNull()
  {
    var npc = new NpcBuilder().Build();
    var entry = new DistributionEntry { NpcFilters = [new FormKeyFilter(Fk(0xDEAD))] };
    Criteria(npc, entry).Should().BeNull();
  }

  [Fact]
  public void FactionFilter_Included_AddsFactionPartWithEditorId()
  {
    var npc = new NpcBuilder
    {
      Factions = [new FactionMembership { FactionFormKey = Fk(0xF1), FactionEditorId = "MyFaction", Rank = 0 }]
    }.Build();
    var entry = new DistributionEntry { FactionFilters = [new FormKeyFilter(Fk(0xF1))] };
    Criteria(npc, entry).Should().Be("Faction: MyFaction");
  }

  [Fact]
  public void KeywordFilter_Included_AddsKeywordPart()
  {
    var npc = new NpcBuilder { Keywords = new HashSet<string> { "ActorTypeNPC" } }.Build();
    var entry = new DistributionEntry { KeywordFilters = [new KeywordFilter("ActorTypeNPC")] };
    Criteria(npc, entry).Should().Be("Keyword: ActorTypeNPC");
  }

  [Fact]
  public void KeywordFilter_VirtualKeyword_AddsKeywordPart()
  {
    var npc = new NpcBuilder().Build();
    var entry = new DistributionEntry { KeywordFilters = [new KeywordFilter("VirtualKw")] };
    var virtualKeywords = new Dictionary<FormKey, HashSet<string>> { [npc.FormKey] = ["VirtualKw"] };
    Criteria(npc, entry, virtualKeywords).Should().Be("Keyword: VirtualKw");
  }

  [Fact]
  public void RaceFilter_Included_AddsRacePartWithEditorId()
  {
    var npc = new NpcBuilder { RaceFormKey = Fk(0x2), RaceEditorId = "NordRace" }.Build();
    var entry = new DistributionEntry { RaceFilters = [new FormKeyFilter(Fk(0x2))] };
    Criteria(npc, entry).Should().Be("Race: NordRace");
  }

  [Fact]
  public void ClassFilter_Match_AddsClassPart()
  {
    var npc = new NpcBuilder { ClassFormKey = Fk(0x3), ClassEditorId = "WarriorClass" }.Build();
    var entry = new DistributionEntry { ClassFormKeys = [Fk(0x3)] };
    Criteria(npc, entry).Should().Be("Class: WarriorClass");
  }

  [Fact]
  public void ClassFilter_NoMatch_ReturnsNull()
  {
    var npc = new NpcBuilder { ClassFormKey = Fk(0x3) }.Build();
    var entry = new DistributionEntry { ClassFormKeys = [Fk(0x99)] };
    Criteria(npc, entry).Should().BeNull();
  }

  [Fact]
  public void OutfitFilter_Match_AddsOutfitPart()
  {
    var npc = new NpcBuilder { DefaultOutfitFormKey = Fk(0x4), DefaultOutfitEditorId = "DefOutfit" }.Build();
    var entry = new DistributionEntry { OutfitFilterFormKeys = [Fk(0x4)] };
    Criteria(npc, entry).Should().Be("Outfit: DefOutfit");
  }

  [Fact]
  public void CombatStyleFilter_Match_AddsCombatStylePart()
  {
    var npc = new NpcBuilder { CombatStyleFormKey = Fk(0x5), CombatStyleEditorId = "csMagic" }.Build();
    var entry = new DistributionEntry { CombatStyleFormKeys = [Fk(0x5)] };
    Criteria(npc, entry).Should().Be("CombatStyle: csMagic");
  }

  [Fact]
  public void VoiceTypeFilter_Match_AddsVoiceTypePart()
  {
    var npc = new NpcBuilder { VoiceTypeFormKey = Fk(0x6), VoiceTypeEditorId = "MaleNord" }.Build();
    var entry = new DistributionEntry { VoiceTypeFormKeys = [Fk(0x6)] };
    Criteria(npc, entry).Should().Be("VoiceType: MaleNord");
  }

  [Fact]
  public void LevelFilter_InRange_AddsLevelPart()
  {
    var npc = new NpcBuilder { Level = 10 }.Build();
    var entry = new DistributionEntry { LevelFilters = "5/20" };
    Criteria(npc, entry).Should().Be("Level: 5/20");
  }

  [Fact]
  public void LevelFilter_OutOfRange_ReturnsNull()
  {
    var npc = new NpcBuilder { Level = 3 }.Build();
    var entry = new DistributionEntry { LevelFilters = "5/20" };
    Criteria(npc, entry).Should().BeNull();
  }

  [Fact]
  public void LevelFilter_None_NotAddedAndMatches()
  {
    var npc = new NpcBuilder().Build();
    var entry = new DistributionEntry { LevelFilters = "NONE" };
    Criteria(npc, entry).Should().Be("All NPCs");
  }

  [Fact]
  public void TraitFilter_FemaleMatch_AddsFemaleTrait()
  {
    var npc = new NpcBuilder { IsFemale = true }.Build();
    var entry = new DistributionEntry { TraitFilters = new SpidTraitFilters { IsFemale = true } };
    Criteria(npc, entry).Should().Be("Female");
  }

  [Fact]
  public void TraitFilter_FemaleNoMatch_ReturnsNull()
  {
    var npc = new NpcBuilder { IsFemale = false }.Build();
    var entry = new DistributionEntry { TraitFilters = new SpidTraitFilters { IsFemale = true } };
    Criteria(npc, entry).Should().BeNull();
  }

  [Fact]
  public void TraitFilter_TeammateDescribedButNotGated()
  {
    var npc = new NpcBuilder().Build();
    var entry = new DistributionEntry { TraitFilters = new SpidTraitFilters { IsTeammate = true } };
    Criteria(npc, entry).Should().Be("Teammate");
  }

  [Fact]
  public void LocationFilter_Match_AddsLocationPart()
  {
    var npc = new NpcBuilder { Locations = new HashSet<FormKey> { Fk(0x7) } }.Build();
    var entry = new DistributionEntry { LocationFormKeys = [Fk(0x7)] };
    Criteria(npc, entry).Should().Be("Location: (1 filter(s))");
  }

  [Fact]
  public void LocationFilter_NoMatch_ReturnsNull()
  {
    var npc = new NpcBuilder { Locations = new HashSet<FormKey> { Fk(0x7) } }.Build();
    var entry = new DistributionEntry { LocationFormKeys = [Fk(0x8)] };
    Criteria(npc, entry).Should().BeNull();
  }

  [Fact]
  public void UnresolvedOnly_NoOtherParts_ReturnsNull()
  {
    var npc = new NpcBuilder().Build();
    var entry = new DistributionEntry { PerkFormKeys = [Fk(0x10)] };
    Criteria(npc, entry).Should().BeNull();
  }

  [Fact]
  public void UnresolvedPerk_WithOtherPart_AddsUnresolvedPart()
  {
    var npc = new NpcBuilder { ClassFormKey = Fk(0x3), ClassEditorId = "WarriorClass" }.Build();
    var entry = new DistributionEntry { ClassFormKeys = [Fk(0x3)], PerkFormKeys = [Fk(0x10)] };
    Criteria(npc, entry).Should().Be("Class: WarriorClass; Unresolved: Perk (1)");
  }

  [Fact]
  public void UnresolvedRawForm_WithOtherPart_AddsFormUnresolved()
  {
    var npc = new NpcBuilder { ClassFormKey = Fk(0x3), ClassEditorId = "WarriorClass" }.Build();
    var entry = new DistributionEntry { ClassFormKeys = [Fk(0x3)], RawFormFilters = "0x123~Other.esp" };
    Criteria(npc, entry).Should().Be("Class: WarriorClass; Unresolved: Form: 0x123~Other.esp");
  }

  [Fact]
  public void MultipleFilters_AssembledInDeclaredOrder()
  {
    var npc = new NpcBuilder
    {
      Name = "Lydia",
      Keywords = new HashSet<string> { "ActorTypeNPC" },
      Factions = [new FactionMembership { FactionFormKey = Fk(0xF1), FactionEditorId = "MyFaction", Rank = 0 }],
      ClassFormKey = Fk(0x3),
      ClassEditorId = "WarriorClass"
    }.Build();

    var entry = new DistributionEntry
    {
      NpcFilters = [new FormKeyFilter(npc.FormKey)],
      FactionFilters = [new FormKeyFilter(Fk(0xF1))],
      KeywordFilters = [new KeywordFilter("ActorTypeNPC")],
      ClassFormKeys = [Fk(0x3)]
    };

    Criteria(npc, entry).Should().Be("NPC: Lydia; Faction: MyFaction; Keyword: ActorTypeNPC; Class: WarriorClass");
  }

  private sealed class NpcBuilder
  {
    public FormKey FormKey { get; init; } = new(TestMod, 0x800);
    public string? Name { get; init; } = "TestNpc";
    public string? EditorId { get; init; } = "TestNpcEditorId";
    public ModKey SourceMod { get; init; } = TestMod;
    public IReadOnlySet<string> Keywords { get; init; } = new HashSet<string>();
    public IReadOnlyList<FactionMembership> Factions { get; init; } = [];
    public FormKey? RaceFormKey { get; init; }
    public string? RaceEditorId { get; init; }
    public FormKey? ClassFormKey { get; init; }
    public string? ClassEditorId { get; init; }
    public FormKey? CombatStyleFormKey { get; init; }
    public string? CombatStyleEditorId { get; init; }
    public FormKey? VoiceTypeFormKey { get; init; }
    public string? VoiceTypeEditorId { get; init; }
    public FormKey? DefaultOutfitFormKey { get; init; }
    public string? DefaultOutfitEditorId { get; init; }
    public bool IsFemale { get; init; }
    public bool IsUnique { get; init; }
    public bool IsSummonable { get; init; }
    public bool IsChild { get; init; }
    public bool IsLeveled { get; init; }
    public short Level { get; init; } = 1;
    public IReadOnlySet<FormKey> Locations { get; init; } = new HashSet<FormKey>();
    public FormKey? TemplateFormKey { get; init; }
    public string? TemplateEditorId { get; init; }
    public HashSet<string>? MatchKeys { get; init; }
    public byte[] SkillValues { get; init; } = new byte[24];

    public NpcFilterData Build() => new()
    {
      FormKey = FormKey,
      EditorId = EditorId,
      Name = Name,
      SourceMod = SourceMod,
      Keywords = Keywords,
      Factions = Factions,
      RaceFormKey = RaceFormKey,
      RaceEditorId = RaceEditorId,
      ClassFormKey = ClassFormKey,
      ClassEditorId = ClassEditorId,
      CombatStyleFormKey = CombatStyleFormKey,
      CombatStyleEditorId = CombatStyleEditorId,
      VoiceTypeFormKey = VoiceTypeFormKey,
      VoiceTypeEditorId = VoiceTypeEditorId,
      DefaultOutfitFormKey = DefaultOutfitFormKey,
      DefaultOutfitEditorId = DefaultOutfitEditorId,
      WornArmorFormKey = null,
      IsFemale = IsFemale,
      IsUnique = IsUnique,
      IsSummonable = IsSummonable,
      IsChild = IsChild,
      IsLeveled = IsLeveled,
      Level = Level,
      Locations = Locations,
      TemplateFormKey = TemplateFormKey,
      TemplateEditorId = TemplateEditorId,
      SkillValues = SkillValues,
      MatchKeys = MatchKeys
    };
  }
}
