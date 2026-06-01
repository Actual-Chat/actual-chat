using ActualChat.Search;

namespace ActualChat.Chat.UnitTests;

public class SearchDocumentQueryTest(ITestOutputHelper @out) : TestBase(@out)
{
    // MemSearchDocument — tokenization

    [Fact]
    public void NormalizeLowercasesAndSpacePrefixesTokens()
    {
        new SearchDocument("John Bolton").PreprocessedText.Should().Be(" john bolton");
        new SearchDocument("bob-john").PreprocessedText.Should().Be(" bob john");
        new SearchDocument("first.last_name").PreprocessedText.Should().Be(" first last name");
        new SearchDocument("  spaced   out  ").PreprocessedText.Should().Be(" spaced out");
        new SearchDocument("-- .. //").PreprocessedText.Should().Be("");
        new SearchDocument("").PreprocessedText.Should().Be("");
        new SearchDocument((string?)null).PreprocessedText.Should().Be("");
    }

    [Fact]
    public void ExpandsCamelCaseAndDigitBoundaries()
    {
        new SearchDocument("McDonalds22").PreprocessedText.Should().Be(" mcdonalds22_donalds22_22");
        new SearchDocument("UIElement3").PreprocessedText.Should().Be(" uielement3_element3_3");
        new SearchDocument("USA50").PreprocessedText.Should().Be(" usa50_50");
        new SearchDocument("iPhone").PreprocessedText.Should().Be(" iphone_phone");
    }

    [Fact]
    public void KeepsAcronymsWhole()
    {
        // A run of uppercase with no trailing lowercase stays one segment.
        new SearchDocument("USA").PreprocessedText.Should().Be(" usa");
        new SearchDocument("HTML").PreprocessedText.Should().Be(" html");
        // The last uppercase before a lowercase starts the next segment.
        new SearchDocument("HTMLParser").PreprocessedText.Should().Be(" htmlparser_parser");
    }

    [Fact]
    public void SplitsLetterDigitBoundaries()
    {
        new SearchDocument("abc123").PreprocessedText.Should().Be(" abc123_123");
        new SearchDocument("123abc").PreprocessedText.Should().Be(" 123abc_abc");
        new SearchDocument("a1b2").PreprocessedText.Should().Be(" a1b2_1b2_b2_2");
    }

    [Fact]
    public void TreatsNonLatinLettersAsWordChars()
    {
        new SearchDocument("Привет Мир").PreprocessedText.Should().Be(" привет мир");
        new SearchDocument("ВебСайт").PreprocessedText.Should().Be(" вебсайт_сайт");
        new SearchDocument("日本語").PreprocessedText.Should().Be(" 日本語");
    }

    [Fact]
    public void RepeatedWordsAreKept()
    {
        // Dedup would need allocations; repeats from splits are acceptable instead.
        new SearchDocument("Funny Funny").PreprocessedText.Should().Be(" funny funny");
    }

    [Fact]
    public void ConcatenatesFragments()
    {
        new SearchDocument("Fusion Place", "Funny Chat").PreprocessedText.Should().Be(" fusion place funny chat");
        new SearchDocument("Foo", null, "", "Bar").PreprocessedText.Should().Be(" foo bar");
    }

    // MemSearchDocument — matching

    [Fact]
    public void MatchesRequiresEveryPrefixToHitAWordStart()
    {
        // Per spec: "J B" matches John Bolton, Bolton John, Bob Johnson — but not Alice Bolton.
        var jb = new SearchQuery("J B");
        new SearchDocument("John Bolton").IsMatch(jb).Should().BeTrue();
        new SearchDocument("Bolton John").IsMatch(jb).Should().BeTrue();
        new SearchDocument("Bob Johnson").IsMatch(jb).Should().BeTrue();
        new SearchDocument("Alice Bolton").IsMatch(jb).Should().BeFalse();
    }

    [Fact]
    public void MatchesIsCaseInsensitive()
    {
        var q = new SearchQuery("J B");
        new SearchDocument("John Bolton").IsMatch(q).Should().BeTrue();
        new SearchDocument("JOHN BOLTON").IsMatch(q).Should().BeTrue();
    }

    [Fact]
    public void MatchesByWordPrefix()
    {
        var doc = new SearchDocument("Hello World");
        doc.IsMatch(new SearchQuery("hel")).Should().BeTrue();
        doc.IsMatch(new SearchQuery("wor")).Should().BeTrue();
        doc.IsMatch(new SearchQuery("world")).Should().BeTrue();
        doc.IsMatch(new SearchQuery("helloworld")).Should().BeFalse();
        doc.IsMatch(new SearchQuery("xyz")).Should().BeFalse();
    }

    [Fact]
    public void MatchesMidWordCamelCaseSegmentButNotInfix()
    {
        var doc = new SearchDocument("McDonalds");
        doc.IsMatch(new SearchQuery("mc")).Should().BeTrue();
        doc.IsMatch(new SearchQuery("donalds")).Should().BeTrue();   // mid-word "_donalds"
        doc.IsMatch(new SearchQuery("onalds")).Should().BeFalse();   // infix — not a token start
    }

    [Fact]
    public void MatchesDigitSuffix()
    {
        var doc = new SearchDocument("Room101");
        doc.IsMatch(new SearchQuery("room")).Should().BeTrue();
        doc.IsMatch(new SearchQuery("101")).Should().BeTrue();    // mid-word "_101"
        doc.IsMatch(new SearchQuery("10")).Should().BeTrue();     // prefix of "_101"
        doc.IsMatch(new SearchQuery("011")).Should().BeFalse();
    }

    [Fact]
    public void EmptyQueryMatchesEverythingAndEmptyDocumentMatchesNothing()
    {
        new SearchDocument("Anything At All").IsMatch(new SearchQuery("")).Should().BeTrue();
        new SearchDocument("").IsMatch(new SearchQuery("x")).Should().BeFalse();
    }

    // MemSearchDocument — coverage scoring

    [Fact]
    public void RanksFullerCoverageHigher()
    {
        var query = new SearchQuery("don");
        var tight = new SearchDocument("Don").GetCoverageScore(query);
        var loose = new SearchDocument("Donovan Smith").GetCoverageScore(query);
        tight.Should().BeGreaterThan(loose);
    }

    [Fact]
    public void RanksExpectedPrefixHigherThanUnexpected()
    {
        // "cd" matches "Cdxx" at an expected word start (" cd") and "AbCd" at an
        // unexpected mid-word boundary ("_cd"); both documents have 4 word chars.
        var query = new SearchQuery("cd");
        var expected = new SearchDocument("Cdxx").GetCoverageScore(query);
        var unexpected = new SearchDocument("AbCd").GetCoverageScore(query);
        expected.Should().BeGreaterThan(unexpected);
    }

    [Fact]
    public void NonMatchOrEmptyQueryScoresZero()
    {
        new SearchDocument("Foo").GetCoverageScore(new SearchQuery("bar")).Should().Be(0d);
        new SearchDocument("Foo").GetCoverageScore(new SearchQuery("")).Should().Be(0d);
    }

    // MemSearchQuery

    [Fact]
    public void QueryIsEmptyForBlankInput()
    {
        new SearchQuery("").IsEmpty.Should().BeTrue();
        new SearchQuery(null).IsEmpty.Should().BeTrue();
        new SearchQuery("   ").IsEmpty.Should().BeTrue();
        new SearchQuery("real").IsEmpty.Should().BeFalse();
        SearchQuery.Empty.IsEmpty.Should().BeTrue();
        default(SearchQuery).IsEmpty.Should().BeTrue();
    }

    // MemSearchDocument — equality

    [Fact]
    public void EqualityComparesNormalizedValue()
    {
        var a = new SearchDocument("Foo Bar");
        var b = new SearchDocument("foo bar");
        var c = new SearchDocument("Other");
        (a == b).Should().BeTrue();
        (a != c).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.Equals(c).Should().BeFalse();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void OrNewKeepsNonEmptyAndBuildsForEmpty()
    {
        var doc = new SearchDocument("Alice");
        doc.OrNew("Bob").Should().Be(doc);
        default(SearchDocument).OrNew("Bob").Should().Be(new SearchDocument("Bob"));
        new SearchDocument("").OrNew("Bob").PreprocessedText.Should().Be(" bob");
    }

    // MemSearchQuery — equality

    [Fact]
    public void QueryEqualityComparesNormalizedValue()
    {
        var a = new SearchQuery("Foo Bar");
        var b = new SearchQuery("foo  bar");
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
        (a == new SearchQuery("other")).Should().BeFalse();
        (default(SearchQuery) == SearchQuery.Empty).Should().BeTrue();
    }

    // MemSearchQuery — highlight parts

    [Fact]
    public void GetMatchPartsEmptyQueryOrNoMatchYieldsNoParts()
    {
        new SearchQuery("").GetMatchParts("Hello World").Should().BeEmpty();
        new SearchQuery("xyz").GetMatchParts("Hello World").Should().BeEmpty();
        new SearchQuery("hello").GetMatchParts("").Should().BeEmpty();
    }

    [Fact]
    public void GetMatchPartsHighlightsWordPrefix()
    {
        GetMatches("Hello World", "wor").Should().Equal("Wor");
        GetMatches("Hello World", "hello world").Should().Equal("Hello", "World");
    }

    [Fact]
    public void GetMatchPartsHighlightsCamelCaseSegment()
    {
        GetMatches("McDonalds", "don").Should().Equal("Don");
        // "mcdon" matches the whole word [0,5) and its inner "don" [2,5) — merged into one.
        GetMatches("McDonalds", "mcdon").Should().Equal("McDon");
    }

    [Fact]
    public void GetMatchPartsHighlightsDigitSegment()
    {
        GetMatches("Room101", "101").Should().Equal("101");
        GetMatches("Room101", "room").Should().Equal("Room");
    }

    [Fact]
    public void GetMatchPartsHighlightsEveryMatchingWordAndDedupsRepeats()
    {
        GetMatches("Alice Anna Adam", "a").Should().Equal("A", "A", "A");
        GetMatches("Hello World", "wor wor").Should().Equal("Wor");
    }

    [Fact]
    public void GetMatchPartsExactModeRequiresWholeToken()
    {
        GetMatches("category cat", "cat", matchSuffixes: false).Should().Equal("cat");
        GetMatches("category cat", "cat").Should().Equal("cat", "cat");
    }

    private static string[] GetMatches(string text, string query, bool matchSuffixes = true)
        => new SearchQuery(query, matchSuffixes)
            .GetMatchParts(text)
            .Select(p => text[p.Range.Start..p.Range.End])
            .ToArray();

    // SearchMatch — query-mode highlighting (the in-chat message highlight path)

    [Fact]
    public void QueryModeSearchMatchLazilyHighlightsAndReportsMatch()
    {
        // HighlightUI produces SearchMatch(text, query); Parts/IsMatch materialize on first use.
        var match = new SearchMatch("Hello World", new SearchQuery("wor"));
        match.IsMatch.Should().BeTrue();
        Spans(match).Should().Equal(("Hello ", false), ("Wor", true), ("ld", false));
    }

    [Fact]
    public void QueryModeSearchMatchHighlightsEveryHighlightedWord()
    {
        // HighlightUI joins a word set with spaces and matches suffixes — several words light up.
        var match = new SearchMatch("Alice Bob Charlie David Emma", new SearchQuery("al ch em"));
        HighlightedSpans(match).Should().Equal("Al", "Ch", "Em");
    }

    [Fact]
    public void EmptyQueryModeSearchMatchHasNoMatchAndOneGapCoveringText()
    {
        SearchMatch.Empty.IsMatch.Should().BeFalse();
        var match = new SearchMatch("Plain text", SearchQuery.Empty);
        match.IsMatch.Should().BeFalse();
        Spans(match).Should().Equal(("Plain text", false));
    }

    [Fact]
    public void PartsWithGapsReconstructsFullText()
    {
        var match = new SearchMatch("Hello World", new SearchQuery("hello world"));
        string.Concat(match.PartsWithGaps.Select(p => match.Text[p.Range.Start..p.Range.End]))
            .Should().Be("Hello World");
    }

    private static (string Text, bool IsHighlighted)[] Spans(SearchMatch match)
        => match.PartsWithGaps.Select(p => (match.Text[p.Range.Start..p.Range.End], p.Rank > 0)).ToArray();

    private static string[] HighlightedSpans(SearchMatch match)
        => match.PartsWithGaps.Where(p => p.Rank > 0)
            .Select(p => match.Text[p.Range.Start..p.Range.End])
            .ToArray();
}
