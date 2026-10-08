using ActualChat.Text;

namespace ActualChat.Chat.UnitTests;

public class LoremIpsumStreamOptionsTest
{
    [Theory]
    [InlineData("/lorem-ipsum-stream", 5, 100, 10, false)]
    [InlineData("/lorem-ipsum-stream 10", 10, 100, 10, false)]
    [InlineData("/lorem-ipsum-stream 2.5", 2.5, 100, 10, false)]
    [InlineData("/lorem-ipsum-stream *200", 5, 200, 10, false)]
    [InlineData("/lorem-ipsum-stream /20", 5, 100, 20, false)]
    [InlineData("/lorem-ipsum-stream 3*50", 3, 50, 10, false)]
    [InlineData("/lorem-ipsum-stream 3/5", 3, 100, 5, false)]
    [InlineData("/lorem-ipsum-stream *50/5", 5, 50, 5, false)]
    [InlineData("/lorem-ipsum-stream 8*40/4", 8, 40, 4, false)]
    [InlineData("/lorem-ipsum-stream with-reset", 5, 100, 10, true)]
    [InlineData("/lorem-ipsum-stream 3 with-reset", 3, 100, 10, true)]
    [InlineData("/lorem-ipsum-stream 2.5*50/20 with-reset", 2.5, 50, 20, true)]
    [InlineData("/lorem-ipsum-stream  with-reset", 5, 100, 10, true)]
    [InlineData("/lorem-ipsum-stream ", 5, 100, 10, false)]
    public void ShouldParseArgumentsAndApplyDefaults(
        string text, double seconds, int cps, int ups, bool mustIncludeReset)
    {
        // act
        var isParsed = LoremIpsumStreamOptions.TryParse(text, out var options);

        // assert
        isParsed.Should().BeTrue();
        options.Should().Be(new LoremIpsumStreamOptions(seconds, cps, ups, mustIncludeReset));
    }

    [Theory]
    [InlineData("/lorem-ipsum-stream abc")]
    [InlineData("/lorem-ipsum-stream 5*")]
    [InlineData("/lorem-ipsum-stream 5/")]
    [InlineData("/lorem-ipsum-stream 5 reset")]
    [InlineData("/lorem-ipsum-stream 5 with-reset extra")]
    [InlineData("/lorem-ipsum-streams")]
    [InlineData("/lorem-ipsum 5")]
    [InlineData("lorem-ipsum-stream")]
    public void ShouldRejectOtherText(string text)
    {
        // act
        var isParsed = LoremIpsumStreamOptions.TryParse(text, out var options);

        // assert
        isParsed.Should().BeFalse();
        options.Should().BeNull();
    }

    [Fact]
    public void ShouldClampExtremeValues()
    {
        // act
        LoremIpsumStreamOptions.TryParse("/lorem-ipsum-stream 100000*100000/100000", out var big);
        LoremIpsumStreamOptions.TryParse("/lorem-ipsum-stream 0*0/0", out var small);

        // assert
        big!.Seconds.Should().Be(LoremIpsumStreamOptions.MaxSeconds);
        big.CharsPerSecond.Should().Be(LoremIpsumStreamOptions.MaxCharsPerSecond);
        big.UpdatesPerSecond.Should().Be(LoremIpsumStreamOptions.MaxUpdatesPerSecond);
        big.TextLength.Should().BeLessThanOrEqualTo(LoremIpsumStreamOptions.MaxTextLength);
        small!.Seconds.Should().Be(LoremIpsumStreamOptions.MinSeconds);
        small.CharsPerSecond.Should().Be(1);
        small.UpdatesPerSecond.Should().Be(1);
        small.TextLength.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("/lorem-ipsum-stream", 500)]
    [InlineData("/lorem-ipsum-stream 10*200", 2000)]
    [InlineData("/lorem-ipsum-stream 2.5*50", 125)]
    public void TextLengthShouldBeSecondsTimesCharsPerSecond(string text, int expected)
    {
        // act
        LoremIpsumStreamOptions.TryParse(text, out var options);

        // assert
        options!.TextLength.Should().Be(expected);
    }
}
