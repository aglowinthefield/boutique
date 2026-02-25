using System.Text;
using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace Boutique.Tests;

public class ExtractedHelperTests
{
    #region AppendSpidFilterPositions

    [Fact]
    public void AppendSpidFilterPositions_AllNull_AppendsNothing()
    {
        var sb = new StringBuilder("Outfit = Test");
        DistributionFileFormatter.AppendSpidFilterPositions(sb, null, null, null);
        sb.ToString().Should().Be("Outfit = Test");
    }

    [Fact]
    public void AppendSpidFilterPositions_SinglePart_AppendsSinglePosition()
    {
        var sb = new StringBuilder("Outfit = Test");
        DistributionFileFormatter.AppendSpidFilterPositions(sb, "ActorTypeNPC");
        sb.ToString().Should().Be("Outfit = Test|ActorTypeNPC");
    }

    [Fact]
    public void AppendSpidFilterPositions_TrailingNulls_AreTrimmed()
    {
        var sb = new StringBuilder("Outfit = Test");
        DistributionFileFormatter.AppendSpidFilterPositions(sb, "ActorTypeNPC", null, null, null);
        sb.ToString().Should().Be("Outfit = Test|ActorTypeNPC");
    }

    [Fact]
    public void AppendSpidFilterPositions_IntermediateNulls_BecomeNone()
    {
        var sb = new StringBuilder("Outfit = Test");
        DistributionFileFormatter.AppendSpidFilterPositions(sb, "ActorTypeNPC", null, null, "F");
        sb.ToString().Should().Be("Outfit = Test|ActorTypeNPC|NONE|NONE|F");
    }

    [Fact]
    public void AppendSpidFilterPositions_AllPopulated_AppendsAll()
    {
        var sb = new StringBuilder("Outfit = Test");
        DistributionFileFormatter.AppendSpidFilterPositions(sb, "Keywords", "Factions", "1/20", "F/-U", null, "50");
        sb.ToString().Should().Be("Outfit = Test|Keywords|Factions|1/20|F/-U|NONE|50");
    }

    [Fact]
    public void AppendSpidFilterPositions_EmptyArray_AppendsNothing()
    {
        var sb = new StringBuilder("Outfit = Test");
        DistributionFileFormatter.AppendSpidFilterPositions(sb);
        sb.ToString().Should().Be("Outfit = Test");
    }

    [Fact]
    public void AppendSpidFilterPositions_OnlyLastPopulated_IntermediatesAreNone()
    {
        var sb = new StringBuilder("Outfit = Test");
        DistributionFileFormatter.AppendSpidFilterPositions(sb, null, null, null, null, null, "75");
        sb.ToString().Should().Be("Outfit = Test|NONE|NONE|NONE|NONE|NONE|75");
    }

    #endregion

    #region AddFormFilterTerm

    [Fact]
    public void AddFormFilterTerm_EmptyList_DoesNotAddTerm()
    {
        var terms = new List<List<string>>();
        DistributionFileFormatter.AddFormFilterTerm([], FilterLogicMode.Or, terms);
        terms.Should().BeEmpty();
    }

    [Fact]
    public void AddFormFilterTerm_OrMode_AddsItemsAsSeparateList()
    {
        var terms = new List<List<string>>();
        DistributionFileFormatter.AddFormFilterTerm(["NordRace", "ImperialRace"], FilterLogicMode.Or, terms);

        terms.Should().HaveCount(1);
        terms[0].Should().BeEquivalentTo(["NordRace", "ImperialRace"]);
    }

    [Fact]
    public void AddFormFilterTerm_AndMode_JoinsWithPlus()
    {
        var terms = new List<List<string>>();
        DistributionFileFormatter.AddFormFilterTerm(["NordRace", "ImperialRace"], FilterLogicMode.And, terms);

        terms.Should().HaveCount(1);
        terms[0].Should().Equal(["NordRace+ImperialRace"]);
    }

    [Fact]
    public void AddFormFilterTerm_SingleItem_SameBehaviorForBothModes()
    {
        var orTerms = new List<List<string>>();
        var andTerms = new List<List<string>>();

        DistributionFileFormatter.AddFormFilterTerm(["NordRace"], FilterLogicMode.Or, orTerms);
        DistributionFileFormatter.AddFormFilterTerm(["NordRace"], FilterLogicMode.And, andTerms);

        orTerms[0].Should().Equal(["NordRace"]);
        andTerms[0].Should().Equal(["NordRace"]);
    }

    #endregion

    #region CollectEditorIdFilters

    [Fact]
    public void CollectEditorIdFilters_IncludedItems_AddedToTerms()
    {
        var items = new ISelectableRecordViewModel[]
        {
            new FakeSelectable("NordRace", isExcluded: false),
            new FakeSelectable("ImperialRace", isExcluded: false)
        };

        var terms = new List<List<string>>();
        var exclusions = new List<string>();

        DistributionFileFormatter.CollectEditorIdFilters(items, FilterLogicMode.Or, terms, exclusions);

        terms.Should().HaveCount(1);
        terms[0].Should().BeEquivalentTo(["NordRace", "ImperialRace"]);
        exclusions.Should().BeEmpty();
    }

    [Fact]
    public void CollectEditorIdFilters_ExcludedItems_AddedToExclusions()
    {
        var items = new ISelectableRecordViewModel[]
        {
            new FakeSelectable("NordRace", isExcluded: true),
            new FakeSelectable("ImperialRace", isExcluded: true)
        };

        var terms = new List<List<string>>();
        var exclusions = new List<string>();

        DistributionFileFormatter.CollectEditorIdFilters(items, FilterLogicMode.Or, terms, exclusions);

        terms.Should().BeEmpty();
        exclusions.Should().BeEquivalentTo(["-NordRace", "-ImperialRace"]);
    }

    [Fact]
    public void CollectEditorIdFilters_MixedItems_SeparatesCorrectly()
    {
        var items = new ISelectableRecordViewModel[]
        {
            new FakeSelectable("NordRace", isExcluded: false),
            new FakeSelectable("OrcRace", isExcluded: true),
            new FakeSelectable("ImperialRace", isExcluded: false)
        };

        var terms = new List<List<string>>();
        var exclusions = new List<string>();

        DistributionFileFormatter.CollectEditorIdFilters(items, FilterLogicMode.And, terms, exclusions);

        terms.Should().HaveCount(1);
        terms[0].Should().Equal(["NordRace+ImperialRace"]);
        exclusions.Should().Equal(["-OrcRace"]);
    }

    [Fact]
    public void CollectEditorIdFilters_SkipsNullAndPlaceholderEditorIds()
    {
        var items = new ISelectableRecordViewModel[]
        {
            new FakeSelectable(null!, isExcluded: false),
            new FakeSelectable("(No EditorID)", isExcluded: false),
            new FakeSelectable("", isExcluded: false),
            new FakeSelectable("ValidRace", isExcluded: false)
        };

        var terms = new List<List<string>>();
        var exclusions = new List<string>();

        DistributionFileFormatter.CollectEditorIdFilters(items, FilterLogicMode.Or, terms, exclusions);

        terms.Should().HaveCount(1);
        terms[0].Should().Equal(["ValidRace"]);
    }

    [Fact]
    public void CollectEditorIdFilters_EmptyCollection_AddsNothing()
    {
        var terms = new List<List<string>>();
        var exclusions = new List<string>();

        DistributionFileFormatter.CollectEditorIdFilters(
            Array.Empty<ISelectableRecordViewModel>(),
            FilterLogicMode.Or,
            terms,
            exclusions);

        terms.Should().BeEmpty();
        exclusions.Should().BeEmpty();
    }

    [Fact]
    public void CollectEditorIdFilters_SkipsPlaceholderExcludedEditorIds()
    {
        var items = new ISelectableRecordViewModel[]
        {
            new FakeSelectable("(No EditorID)", isExcluded: true),
            new FakeSelectable("", isExcluded: true)
        };

        var terms = new List<List<string>>();
        var exclusions = new List<string>();

        DistributionFileFormatter.CollectEditorIdFilters(items, FilterLogicMode.Or, terms, exclusions);

        exclusions.Should().BeEmpty();
    }

    #endregion

    private class FakeSelectable(string editorId, bool isExcluded) : ISelectableRecordViewModel
    {
        public FormKey FormKey => FormKey.Null;
        public string EditorID => editorId;
        public string DisplayName => editorId;
        public string ModDisplayName => string.Empty;
        public string FormKeyString => string.Empty;
        public bool IsSelected { get; set; }
        public bool IsExcluded { get; set; } = isExcluded;
        public bool MatchesSearch(string searchTerm) => true;
    }
}
