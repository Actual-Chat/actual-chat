namespace ActualChat.Chat.UnitTests;

public class MemSearchTest(ITestOutputHelper @out) : TestBase(@out)
{
    // MemSearchDocument — tokenization

    [Fact]
    public void NormalizeLowercasesAndSpacePrefixesTokens()
    {
        new MemSearchDocument("John Bolton").Value.Should().Be(" john bolton");
        new MemSearchDocument("bob-john").Value.Should().Be(" bob john");
        new MemSearchDocument("first.last_name").Value.Should().Be(" first last name");
        new MemSearchDocument("  spaced   out  ").Value.Should().Be(" spaced out");
        new MemSearchDocument("-- .. //").Value.Should().Be("");
        new MemSearchDocument("").Value.Should().Be("");
        new MemSearchDocument((string?)null).Value.Should().Be("");
    }

    [Fact]
    public void ExpandsCamelCaseAndDigitBoundaries()
    {
        new MemSearchDocument("McDonalds22").Value.Should().Be(" mcdonalds22_donalds22_22");
        new MemSearchDocument("UIElement3").Value.Should().Be(" uielement3_element3_3");
        new MemSearchDocument("USA50").Value.Should().Be(" usa50_50");
        new MemSearchDocument("iPhone").Value.Should().Be(" iphone_phone");
    }

    [Fact]
    public void KeepsAcronymsWhole()
    {
        // A run of uppercase with no trailing lowercase stays one segment.
        new MemSearchDocument("USA").Value.Should().Be(" usa");
        new MemSearchDocument("HTML").Value.Should().Be(" html");
        // The last uppercase before a lowercase starts the next segment.
        new MemSearchDocument("HTMLParser").Value.Should().Be(" htmlparser_parser");
    }

    [Fact]
    public void SplitsLetterDigitBoundaries()
    {
        new MemSearchDocument("abc123").Value.Should().Be(" abc123_123");
        new MemSearchDocument("123abc").Value.Should().Be(" 123abc_abc");
        new MemSearchDocument("a1b2").Value.Should().Be(" a1b2_1b2_b2_2");
    }

    [Fact]
    public void TreatsNonLatinLettersAsWordChars()
    {
        new MemSearchDocument("Привет Мир").Value.Should().Be(" привет мир");
        new MemSearchDocument("ВебСайт").Value.Should().Be(" вебсайт_сайт");
        new MemSearchDocument("日本語").Value.Should().Be(" 日本語");
    }

    [Fact]
    public void RepeatedWordsAreKept()
    {
        // Dedup would need allocations; repeats from splits are acceptable instead.
        new MemSearchDocument("Funny Funny").Value.Should().Be(" funny funny");
    }

    [Fact]
    public void ConcatenatesFragments()
    {
        new MemSearchDocument("Fusion Place", "Funny Chat").Value.Should().Be(" fusion place funny chat");
        new MemSearchDocument("Foo", null, "", "Bar").Value.Should().Be(" foo bar");
    }

    // MemSearchDocument — matching

    [Fact]
    public void MatchesRequiresEveryPrefixToHitAWordStart()
    {
        // Per spec: "J B" matches John Bolton, Bolton John, Bob Johnson — but not Alice Bolton.
        var jb = new MemSearchQuery("J B");
        new MemSearchDocument("John Bolton").IsMatch(jb).Should().BeTrue();
        new MemSearchDocument("Bolton John").IsMatch(jb).Should().BeTrue();
        new MemSearchDocument("Bob Johnson").IsMatch(jb).Should().BeTrue();
        new MemSearchDocument("Alice Bolton").IsMatch(jb).Should().BeFalse();
    }

    [Fact]
    public void MatchesIsCaseInsensitive()
    {
        var q = new MemSearchQuery("J B");
        new MemSearchDocument("John Bolton").IsMatch(q).Should().BeTrue();
        new MemSearchDocument("JOHN BOLTON").IsMatch(q).Should().BeTrue();
    }

    [Fact]
    public void MatchesByWordPrefix()
    {
        var doc = new MemSearchDocument("Hello World");
        doc.IsMatch(new MemSearchQuery("hel")).Should().BeTrue();
        doc.IsMatch(new MemSearchQuery("wor")).Should().BeTrue();
        doc.IsMatch(new MemSearchQuery("world")).Should().BeTrue();
        doc.IsMatch(new MemSearchQuery("helloworld")).Should().BeFalse();
        doc.IsMatch(new MemSearchQuery("xyz")).Should().BeFalse();
    }

    [Fact]
    public void MatchesMidWordCamelCaseSegmentButNotInfix()
    {
        var doc = new MemSearchDocument("McDonalds");
        doc.IsMatch(new MemSearchQuery("mc")).Should().BeTrue();
        doc.IsMatch(new MemSearchQuery("donalds")).Should().BeTrue();   // mid-word "_donalds"
        doc.IsMatch(new MemSearchQuery("onalds")).Should().BeFalse();   // infix — not a token start
    }

    [Fact]
    public void MatchesDigitSuffix()
    {
        var doc = new MemSearchDocument("Room101");
        doc.IsMatch(new MemSearchQuery("room")).Should().BeTrue();
        doc.IsMatch(new MemSearchQuery("101")).Should().BeTrue();    // mid-word "_101"
        doc.IsMatch(new MemSearchQuery("10")).Should().BeTrue();     // prefix of "_101"
        doc.IsMatch(new MemSearchQuery("011")).Should().BeFalse();
    }

    [Fact]
    public void EmptyQueryMatchesEverythingAndEmptyDocumentMatchesNothing()
    {
        new MemSearchDocument("Anything At All").IsMatch(new MemSearchQuery("")).Should().BeTrue();
        new MemSearchDocument("").IsMatch(new MemSearchQuery("x")).Should().BeFalse();
    }

    // MemSearchDocument — coverage scoring

    [Fact]
    public void RanksFullerCoverageHigher()
    {
        var query = new MemSearchQuery("don");
        var tight = new MemSearchDocument("Don").GetCoverageScore(query);
        var loose = new MemSearchDocument("Donovan Smith").GetCoverageScore(query);
        tight.Should().BeGreaterThan(loose);
    }

    [Fact]
    public void RanksExpectedPrefixHigherThanUnexpected()
    {
        // "cd" matches "Cdxx" at an expected word start (" cd") and "AbCd" at an
        // unexpected mid-word boundary ("_cd"); both documents have 4 word chars.
        var query = new MemSearchQuery("cd");
        var expected = new MemSearchDocument("Cdxx").GetCoverageScore(query);
        var unexpected = new MemSearchDocument("AbCd").GetCoverageScore(query);
        expected.Should().BeGreaterThan(unexpected);
    }

    [Fact]
    public void NonMatchOrEmptyQueryScoresZero()
    {
        new MemSearchDocument("Foo").GetCoverageScore(new MemSearchQuery("bar")).Should().Be(0d);
        new MemSearchDocument("Foo").GetCoverageScore(new MemSearchQuery("")).Should().Be(0d);
    }

    // MemSearchQuery

    [Fact]
    public void QueryIsEmptyForBlankInput()
    {
        new MemSearchQuery("").IsEmpty.Should().BeTrue();
        new MemSearchQuery(null).IsEmpty.Should().BeTrue();
        new MemSearchQuery("   ").IsEmpty.Should().BeTrue();
        new MemSearchQuery("real").IsEmpty.Should().BeFalse();
        MemSearchQuery.Empty.IsEmpty.Should().BeTrue();
        default(MemSearchQuery).IsEmpty.Should().BeTrue();
    }

    // MemSearchDocument — equality

    [Fact]
    public void EqualityComparesNormalizedValue()
    {
        var a = new MemSearchDocument("Foo Bar");
        var b = new MemSearchDocument("foo bar");
        var c = new MemSearchDocument("Other");
        (a == b).Should().BeTrue();
        (a != c).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.Equals(c).Should().BeFalse();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
