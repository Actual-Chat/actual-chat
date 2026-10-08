using ActualChat.Text;

namespace ActualChat.Chat.UnitTests;

public class LoremIpsumMarkupSampleTest
{
    private const string Reset = "<!--reset-->";

    private static readonly IMarkupParser Parser = new MarkupParser();
    private static readonly IMarkupParser IncompleteParser = new MarkupParser { AllowIncompleteMarkup = true };

    [Fact]
    public void SampleShouldCoverEveryMarkupKindEveryTime()
    {
        for (var i = 0; i < 30; i++)
            AssertCoversEveryMarkupKind(Parser.Parse(LoremIpsum.GetMarkupSample(3000, false)));
    }

    [Fact]
    public void SampleShouldKeepMixingMarkupAllTheWay()
    {
        for (var i = 0; i < 30; i++) {
            // arrange
            var markup = Parser.Parse(LoremIpsum.GetMarkupSample(8000, false));

            // act
            var items = markup.Should().BeOfType<MarkupSeq>().Subject.Items;
            var lastItems = items.Skip(items.Length * 2 / 3).ToList();
            var kinds = lastItems.Select(x => x.GetType().Name).Distinct().ToList();

            // assert
            lastItems.Count.Should().BeGreaterThan(10);
            var blockKinds = kinds.Count(x => x != nameof(ParagraphMarkup));
            blockKinds.Should().BeGreaterThanOrEqualTo(3, $"the last third has only {string.Join(", ", kinds)}");
            Has(new MarkupSeq(lastItems.ToArray()), m => m is StylizedMarkup or UrlMarkup or HashtagMarkup)
                .Should().BeTrue("the last third must still have inline markup");
        }
    }

    [Fact]
    public void SamplesShouldDiffer()
    {
        // act
        var samples = Enumerable.Range(0, 10).Select(_ => LoremIpsum.GetMarkupSample(1500, false)).Distinct();

        // assert
        samples.Count().Should().BeGreaterThan(8);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(3000)]
    public void SampleShouldHaveTheRequestedLength(int length)
    {
        // act
        var sample = LoremIpsum.GetMarkupSample(length, false);

        // assert
        sample.Length.Should().Be(length);
    }

    [Fact]
    public void SampleShouldHaveNoResetUnlessAsked()
    {
        // act
        var samples = Enumerable.Range(0, 20).Select(_ => LoremIpsum.GetMarkupSample(1000, false));

        // assert
        samples.Should().OnlyContain(x => !x.Contains(Reset));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(500)]
    [InlineData(3000)]
    public void SampleWithResetShouldHaveExactlyOneResetOnItsOwnLineOutsideCode(int length)
    {
        for (var i = 0; i < 50; i++) {
            // act
            var sample = LoremIpsum.GetMarkupSample(length, true);
            var markup = Parser.Parse(sample);

            // assert
            sample.Split('\n').Count(line => line == Reset).Should().Be(1);
            sample.Split(Reset).Length.Should().Be(2);
            markup.Format().Should().NotContain("<!--", "a reset inside code would be kept as code");
        }
    }

    [Fact]
    public void ResetShouldBeInsertableAtEveryLength()
    {
        // A sample cut inside its first code block has no line to put the marker after
        for (var length = 1; length <= 400; length++) {
            for (var i = 0; i < 5; i++) {
                // act
                var sample = LoremIpsum.GetMarkupSample(length, true);

                // assert
                sample.Split('\n').Count(line => line == Reset).Should().Be(1, $"length {length}");
                Parser.Parse(sample).Format().Should().NotContain("<!--", $"length {length}");
            }
        }
    }

    [Fact]
    public void ResetShouldLandInDifferentPlaces()
    {
        // act
        var positions = Enumerable.Range(0, 100)
            .Select(_ => LoremIpsum.GetMarkupSample(3000, true).IndexOf(Reset))
            .Distinct()
            .Count();

        // assert
        positions.Should().BeGreaterThan(3);
    }

    [Theory]
    [InlineData(500, 100, 10)]
    [InlineData(1000, 200, 20)]
    [InlineData(300, 30, 3)]
    public void StreamPlanShouldReassembleToTheTextAtTheRequestedRates(int length, int cps, int ups)
    {
        // arrange
        var text = LoremIpsum.GetMarkupSample(length, false);

        // act
        var plan = LoremIpsum.GetStreamPlan(text, cps, ups).ToList();

        // assert
        string.Concat(plan.Select(x => x.Chunk)).Should().Be(text);
        var seconds = (double)length / cps;
        plan.Sum(x => x.Delay.TotalSeconds).Should().BeApproximately(seconds, seconds * 0.35);
        plan.Count.Should().BeInRange((int)(seconds * ups * 0.6), (int)(seconds * ups * 1.4));
        plan.Should().OnlyContain(x => x.Chunk.Length > 0 && x.Delay > TimeSpan.Zero);
    }

    [Fact]
    public void StreamPlanShouldFluctuateAroundTheUpdateRate()
    {
        // arrange
        var text = LoremIpsum.GetMarkupSample(2000, false);

        // act
        var plan = LoremIpsum.GetStreamPlan(text, 100, 10).ToList();

        // assert
        plan.Select(x => x.Delay).Distinct().Count().Should().BeGreaterThan(plan.Count / 2);
        plan.Select(x => x.Chunk.Length).Distinct().Count().Should().BeGreaterThan(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryStreamedPrefixShouldParseWithoutShowingTheMarker(bool mustIncludeReset)
    {
        // arrange
        var text = LoremIpsum.GetMarkupSample(1500, mustIncludeReset);

        // act & assert
        var prefix = "";
        foreach (var (chunk, _) in LoremIpsum.GetStreamPlan(text, 1000, 100)) {
            prefix += chunk;
            var markup = IncompleteParser.Parse(prefix);
            markup.ToReadableText().Should().NotContain("<!--", $"after {prefix.Length} chars");
        }
    }

    // Private methods

    private static void AssertCoversEveryMarkupKind(Markup markup)
    {
        Has(markup, m => m is HeaderMarkup { Level: 1 }).Should().BeTrue("h1");
        Has(markup, m => m is HeaderMarkup { Level: 2 }).Should().BeTrue("h2");
        Has(markup, m => m is HeaderMarkup { Level: 3 }).Should().BeTrue("h3");
        Has(markup, m => m is StylizedMarkup { Style: TextStyle.Bold }).Should().BeTrue("bold");
        Has(markup, m => m is StylizedMarkup { Style: TextStyle.Italic }).Should().BeTrue("italic");
        Has(markup, m => m is StylizedMarkup { Style: TextStyle.Spoiler }).Should().BeTrue("spoiler");
        Has(markup, m => m is PreformattedTextMarkup).Should().BeTrue("inline code");
        Has(markup, m => m is CodeBlockMarkup { Language.Length: > 0 }).Should().BeTrue("code block");
        Has(markup, m => m is ListMarkup).Should().BeTrue("list");
        HasQuote(markup, false).Should().BeTrue("quote");
        HasQuote(markup, true).Should().BeTrue("nested quote");
        Has(markup, m => m is TableMarkup { Alignments.Length: > 1 }).Should().BeTrue("table");
        Has(markup, m => m is DividerMarkup).Should().BeTrue("divider");
        Has(markup, m => m is HashtagMarkup).Should().BeTrue("hashtag");
        Has(markup, m => m is UrlMarkup { Title: not null }).Should().BeTrue("titled link");
        Has(markup, m => m is UrlMarkup { IsEnclosed: true }).Should().BeTrue("explicit link");
        Has(markup, m => m is UrlMarkup { Title: null, IsEnclosed: false, Kind: UrlMarkupKind.Www })
            .Should().BeTrue("bare url");
        Has(markup, m => m is UrlMarkup { Kind: UrlMarkupKind.Email }).Should().BeTrue("email");
    }

    private static bool HasQuote(Markup markup, bool isNested)
    {
        // MarkupValidator doesn't offer a quote to its predicate, so the top-level blocks are looked at
        var blocks = markup is MarkupSeq seq ? seq.Items : [markup];
        return blocks.OfType<BlockQuoteMarkup>().Any(q => !isNested
            || (q.Content is MarkupSeq inner && inner.Items.Any(x => x is BlockQuoteMarkup)));
    }

    private static bool Has(Markup markup, Func<Markup, bool> predicate)
        => new MarkupValidator(predicate, MarkupValidator.AggregationMode.Any).IsValid(markup);
}
