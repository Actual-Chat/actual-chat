using System.Globalization;
using ActualLab.Mathematics;

namespace ActualChat.Search.UnitTests;

public class HighlightsConverterTest
{
    private const string Pre = HighlightsConverter.PreTag;
    private const string Post = HighlightsConverter.PostTag;

    [Theory]
    [InlineData("Jack Stone", $"{Pre}Jack{Post} Stone", "0,4")]
    [InlineData("Emily Yellow", $"{Pre}Emily{Post} {Pre}Yellow{Post}", "0,5", "6,12")]
    [InlineData("Should find text to highlight in this long text content", $"Should find {Pre}text{Post} to highlight in this long {Pre}text{Post} {Pre}content{Post}", "12,16", "43,47", "48,55")]
    public void ShouldFindMatches(string plain, string highlight, params string[] expectedSRanges)
    {
        // arrange
        var expectedRanges = expectedSRanges.Select(ParseRange).Select(x => new SearchMatchPart(x, 1)).ToArray();

        // act
        var searchMatch = HighlightsConverter.ToSearchMatch(plain, highlight, 0.5);

        // assert
        searchMatch.Should().BeEquivalentTo(new SearchMatch(plain, 0.5, expectedRanges));
    }

    private Range<int> ParseRange(string sRange)
    {
        var parts = sRange.Split(',');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }
}
