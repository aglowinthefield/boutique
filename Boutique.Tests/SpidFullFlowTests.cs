using Boutique.Models;
using Boutique.Utilities;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Boutique.Tests;

/// <summary>
///     Full-flow integration tests that verify the complete pipeline:
///     Parse file → Extract data → Format back → Verify semantics
///     These tests use real distribution files to catch regressions.
/// </summary>
public class SpidFullFlowTests(ITestOutputHelper output)
{
    private static readonly string TestDataPath = Path.Combine(
        AppContext.BaseDirectory, "TestData", "Spid");

    #region File-Based Parameterized Round-Trip Tests

    [Theory]
    [MemberData(nameof(GetAllTestDataLines))]
    public void RoundTrip_AllTestDataLines_Preserved(string fileName, int lineNumber, string line)
    {
        var parsed = SpidLineParser.TryParse(line, out var filter);
        parsed.Should().BeTrue($"line {lineNumber} in {fileName} should parse: {line}");

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
        formatted.Should().Be(line);
    }

    public static IEnumerable<object[]> GetAllTestDataLines()
    {
        if (!Directory.Exists(TestDataPath))
        {
            yield break;
        }

        foreach (var file in Directory.GetFiles(TestDataPath, "*.ini"))
        {
            var fileName = Path.GetFileName(file);
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';'))
                {
                    continue;
                }

                yield return [fileName, i + 1, line];
            }
        }
    }

    #endregion

    #region Magecore Full Flow Tests

    [Fact]
    public void FullFlow_MagecoreFile_ParsesAllLinesAndRoundTrips()
    {
        var filePath = Path.Combine(TestDataPath, "Magecore_General_DISTR.ini");
        if (!File.Exists(filePath))
        {
            return;
        }

        var lines = File.ReadAllLines(filePath);
        var results = new List<(int Line, string Original, string? Formatted, bool Success, string? Error)>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';'))
            {
                continue;
            }

            if (!SpidLineParser.TryParse(line, out var filter))
            {
                results.Add((i + 1, line, null, false, "Parse failed"));
                continue;
            }

            var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
            var success = formatted == line;
            results.Add((i + 1, line, formatted, success, success ? null : "Mismatch"));
        }

        var failures = results.Where(r => !r.Success).ToList();
        output.WriteLine($"Total lines tested: {results.Count}");
        output.WriteLine($"Passed: {results.Count(r => r.Success)}");
        output.WriteLine($"Failed: {failures.Count}");

        foreach (var failure in failures.Take(10))
        {
            output.WriteLine($"Line {failure.Line}: {failure.Error}");
            output.WriteLine($"  Original:  {failure.Original}");
            output.WriteLine($"  Formatted: {failure.Formatted}");
        }

        failures.Should().BeEmpty("all lines should round-trip correctly");
    }

    [Fact]
    public void FullFlow_MagecoreFile_ExtractsVirtualKeywordDependencies()
    {
        var filePath = Path.Combine(TestDataPath, "Magecore_General_DISTR.ini");
        if (!File.Exists(filePath))
        {
            return;
        }

        var lines = File.ReadAllLines(filePath);
        var parsedFilters = lines
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith(';'))
            .Select(l => SpidLineParser.TryParse(l, out var f) ? f : null)
            .Where(f => f != null)
            .ToList();

        var definedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var referencedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var undefinedReferences = new List<string>();

        foreach (var filter in parsedFilters)
        {
            if (filter!.FormType == SpidFormType.Keyword)
            {
                definedKeywords.Add(filter.FormIdentifier);
            }

            var allParts = filter.StringFilters.Expressions
                .SelectMany(e => e.Parts)
                .Concat(filter.StringFilters.GlobalExclusions)
                .Select(p => p.Value);

            foreach (var part in allParts)
            {
                if (part.StartsWith("MAGECORE_", StringComparison.OrdinalIgnoreCase))
                {
                    var cleanPart = part.TrimStart('*');
                    referencedKeywords.Add(cleanPart);
                }
            }
        }

        foreach (var reference in referencedKeywords)
        {
            if (!definedKeywords.Contains(reference))
            {
                undefinedReferences.Add(reference);
            }
        }

        output.WriteLine($"Defined virtual keywords: {definedKeywords.Count}");
        output.WriteLine($"Referenced virtual keywords: {referencedKeywords.Count}");
        output.WriteLine($"Undefined references: {undefinedReferences.Count}");

        foreach (var undef in undefinedReferences)
        {
            output.WriteLine($"  - {undef}");
        }

        definedKeywords.Should().HaveCountGreaterThanOrEqualTo(40, "Magecore should define 40+ virtual keywords");
        referencedKeywords.Should().HaveCountGreaterThanOrEqualTo(30, "Magecore should reference 30+ virtual keywords");
    }

    [Fact]
    public void FullFlow_MagecoreFile_VerifiesSkillLevelPatterns()
    {
        var filePath = Path.Combine(TestDataPath, "Magecore_General_DISTR.ini");
        if (!File.Exists(filePath))
        {
            return;
        }

        var lines = File.ReadAllLines(filePath);
        var parsedFilters = lines
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith(';'))
            .Select(l => SpidLineParser.TryParse(l, out var f) ? f : null)
            .Where(f => f != null && !string.IsNullOrEmpty(f.LevelFilters))
            .ToList();

        var skillFilters = parsedFilters
            .Where(f => f!.LevelFilters!.Contains('('))
            .ToList();

        var rangeFilters = parsedFilters
            .Where(f => f!.LevelFilters!.Contains('/') && !f.LevelFilters.Contains('('))
            .ToList();

        output.WriteLine($"Skill-based level filters (with parentheses): {skillFilters.Count}");
        output.WriteLine($"Range-based level filters: {rangeFilters.Count}");

        foreach (var filter in skillFilters.Take(5))
        {
            output.WriteLine($"  {filter!.FormIdentifier}: {filter.LevelFilters}");
        }

        skillFilters.Should().HaveCountGreaterThanOrEqualTo(20, "Magecore should have 20+ skill-based filters");
        rangeFilters.Should().HaveCountGreaterThanOrEqualTo(5, "Magecore should have 5+ range-based filters");
    }

    [Fact]
    public void FullFlow_MagecoreFile_VerifiesGroupDistribution()
    {
        var filePath = Path.Combine(TestDataPath, "Magecore_General_DISTR.ini");
        if (!File.Exists(filePath))
        {
            return;
        }

        var lines = File.ReadAllLines(filePath);
        var parsedFilters = lines
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith(';'))
            .Select(l => SpidLineParser.TryParse(l, out var f) ? f : null)
            .Where(f => f != null)
            .ToList();

        var groupAOutfits = parsedFilters
            .Where(f => f!.FormType == SpidFormType.Outfit)
            .Where(f => f!.StringFilters.Expressions.Any(e =>
                e.Parts.Any(p => p.Value == "MAGECORE_isGroupA")))
            .ToList();

        var groupBOutfits = parsedFilters
            .Where(f => f!.FormType == SpidFormType.Outfit)
            .Where(f => f!.StringFilters.Expressions.Any(e =>
                e.Parts.Any(p => p.Value == "MAGECORE_isGroupB")))
            .ToList();

        var groupCOutfits = parsedFilters
            .Where(f => f!.FormType == SpidFormType.Outfit)
            .Where(f => f!.StringFilters.Expressions.Any(e =>
                e.Parts.Any(p => p.Value == "MAGECORE_isGroupC")))
            .ToList();

        output.WriteLine($"GroupA outfits: {groupAOutfits.Count}");
        output.WriteLine($"GroupB outfits: {groupBOutfits.Count}");
        output.WriteLine($"GroupC outfits: {groupCOutfits.Count}");

        groupAOutfits.Should().HaveCountGreaterThanOrEqualTo(25, "GroupA should have 25+ outfits");
        groupBOutfits.Should().HaveCountGreaterThanOrEqualTo(25, "GroupB should have 25+ outfits");
        groupCOutfits.Should().HaveCountGreaterThanOrEqualTo(25, "GroupC should have 25+ outfits");
        groupAOutfits.Should().HaveSameCount(groupBOutfits);
        groupBOutfits.Should().HaveSameCount(groupCOutfits);
    }

    #endregion

    #region Sample File Full Flow Tests

    [Fact]
    public void FullFlow_SampleFile_AllFormTypesPresent()
    {
        var filePath = Path.Combine(TestDataPath, "sample_DISTR.ini");
        if (!File.Exists(filePath))
        {
            return;
        }

        var lines = File.ReadAllLines(filePath);
        var parsedFilters = lines
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith(';'))
            .Select(l => SpidLineParser.TryParse(l, out var f) ? f : null)
            .Where(f => f != null)
            .ToList();

        var formTypes = parsedFilters
            .GroupBy(f => f!.FormType)
            .ToDictionary(g => g.Key, g => g.Count());

        output.WriteLine("Form types found:");
        foreach (var kvp in formTypes.OrderBy(k => k.Key.ToString()))
        {
            output.WriteLine($"  {kvp.Key}: {kvp.Value}");
        }

        formTypes.Keys.Should().Contain(SpidFormType.Outfit);
        formTypes.Keys.Should().Contain(SpidFormType.Keyword);
    }

    [Fact]
    public void FullFlow_SampleFile_FormKeyFormatsPreserved()
    {
        var filePath = Path.Combine(TestDataPath, "sample_DISTR.ini");
        if (!File.Exists(filePath))
        {
            return;
        }

        var lines = File.ReadAllLines(filePath);
        var formKeyLines = lines
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith(';'))
            .Where(l => l.Contains("0x") || l.Contains("~"))
            .ToList();

        foreach (var line in formKeyLines)
        {
            var parsed = SpidLineParser.TryParse(line.Trim(), out var filter);
            parsed.Should().BeTrue($"line should parse: {line}");

            var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
            formatted.Should().Be(line.Trim());
        }

        output.WriteLine($"FormKey lines tested: {formKeyLines.Count}");
    }

    #endregion

    #region Cross-File Consistency Tests

    [Fact]
    public void FullFlow_AllFiles_NoParseFailures()
    {
        if (!Directory.Exists(TestDataPath))
        {
            return;
        }

        var allFailures = new List<(string File, int Line, string Content)>();

        foreach (var file in Directory.GetFiles(TestDataPath, "*.ini"))
        {
            var fileName = Path.GetFileName(file);
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';'))
                {
                    continue;
                }

                if (!SpidLineParser.TryParse(line, out _))
                {
                    allFailures.Add((fileName, i + 1, line));
                }
            }
        }

        output.WriteLine($"Total parse failures: {allFailures.Count}");
        foreach (var failure in allFailures)
        {
            output.WriteLine($"  {failure.File}:{failure.Line}: {failure.Content}");
        }

        allFailures.Should().BeEmpty("all lines in test data should parse");
    }

    [Fact]
    public void FullFlow_AllFiles_RoundTripConsistency()
    {
        if (!Directory.Exists(TestDataPath))
        {
            return;
        }

        var allMismatches = new List<(string File, int Line, string Original, string Formatted)>();

        foreach (var file in Directory.GetFiles(TestDataPath, "*.ini"))
        {
            var fileName = Path.GetFileName(file);
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';'))
                {
                    continue;
                }

                if (!SpidLineParser.TryParse(line, out var filter))
                {
                    continue;
                }

                var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
                if (formatted != line)
                {
                    allMismatches.Add((fileName, i + 1, line, formatted));
                }
            }
        }

        output.WriteLine($"Total round-trip mismatches: {allMismatches.Count}");
        foreach (var mismatch in allMismatches.Take(10))
        {
            output.WriteLine($"  {mismatch.File}:{mismatch.Line}");
            output.WriteLine($"    Original:  {mismatch.Original}");
            output.WriteLine($"    Formatted: {mismatch.Formatted}");
        }

        allMismatches.Should().BeEmpty("all lines should round-trip consistently");
    }

    #endregion
}
