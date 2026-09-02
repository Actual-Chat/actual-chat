namespace ActualChat.Core.UnitTests;

public class LocalUrlTest
{
    [Theory]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("/chat", "/chat")]
    [InlineData("chat", "/chat")]
    [InlineData("/chat/", "/chat")]
    [InlineData("/chat?x=1#f", "/chat?x=1#f")]
    // Construction never fails - these normalize, and IsTrulyLocal is what rejects them
    [InlineData("//evil.example", "//evil.example")]
    [InlineData("/\\evil.example", "/\\evil.example")]
    [InlineData("https://evil.example", "/https://evil.example")]
    public void ConstructionNormalizesAnything(string value, string expected)
    {
        // act
        var localUrl = LocalUrl.Parse(value);

        // assert
        localUrl.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/chat")]
    [InlineData("/chat?x=1#f")]
    [InlineData("/https://evil.example")] // A path, not another origin
    public void IsTrulyLocalAcceptsLocalPaths(string value)
    {
        // act
        var isTrulyLocal = LocalUrl.Parse(value).IsTrulyLocal();

        // assert
        isTrulyLocal.Should().BeTrue();
    }

    [Theory]
    [InlineData("//evil.example")] // Protocol-relative
    [InlineData("/\\evil.example")] // Browsers read the backslash as a slash
    [InlineData("//evil.example/path")]
    [InlineData("/\tx")] // Browsers strip tab/LF/CR before parsing
    [InlineData("/\nx")]
    [InlineData("/\rx")]
    public void IsTrulyLocalRejectsCrossOriginForms(string value)
    {
        // act
        var localUrl = LocalUrl.Parse(value);
        var isTrulyLocal = localUrl.IsTrulyLocal();
        var assert = () => localUrl.AssertTrulyLocal();

        // assert
        isTrulyLocal.Should().BeFalse();
        assert.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AssertLocalAcceptsNormalizedUrls()
    {
        // act
        var localUrl = LocalUrl.Parse("chat").AssertLocal();
        var protocolRelative = LocalUrl.Parse("//evil.example").AssertLocal();

        // assert
        localUrl.Value.Should().Be("/chat");
        protocolRelative.Value.Should().Be("//evil.example");
    }

    [Theory]
    [InlineData("/chat", "/chat")]
    [InlineData("/chat?x=1#f", "/chat?x=1#f")]
    [InlineData("https://voxt.ai/chat", "/chat")]
    [InlineData("https://voxt.ai/chat?x=1#f", "/chat?x=1#f")]
    public void TryParseWithOriginAcceptsSameOrigin(string value, string expected)
    {
        // arrange
        var origin = new Uri("https://voxt.ai");

        // act
        var isParsed = LocalUrl.TryParse(value, origin, out var result);

        // assert
        isParsed.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("https://evil.example/chat")]
    [InlineData("http://voxt.ai/chat")] // Scheme differs
    [InlineData("https://voxt.ai:8443/chat")] // Port differs
    [InlineData("/\tchat")]
    public void TryParseWithOriginRejectsEverythingElse(string value)
    {
        // arrange
        var origin = new Uri("https://voxt.ai");

        // act
        var isParsed = LocalUrl.TryParse(value, origin, out _);

        // assert
        isParsed.Should().BeFalse();
    }

    [Theory]
    [InlineData("https://voxt.ai/chat/x", "/chat/x")]
    [InlineData("http://voxt.ai/chat/x", "/chat/x")] // Android resolves a bare "voxt.ai" tap to http
    [InlineData("http://voxt.ai/", "/")]
    [InlineData("HTTP://VOXT.AI/chat/x", "/chat/x")]
    [InlineData("http://voxt.ai/chat?x=1#f", "/chat?x=1#f")]
    public void TryParseAppLinkShouldAcceptEitherWebScheme(string value, string expected)
    {
        // arrange
        var origin = new Uri("https://voxt.ai");

        // act
        var isParsed = LocalUrl.TryParseAppLink(value, origin, out var result);

        // assert
        isParsed.Should().BeTrue("the intent filter advertises http and https alike");
        result.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://evil.example/chat")]
    [InlineData("https://voxt.ai.evil.example/chat")] // Shares the origin's prefix
    [InlineData("ftp://voxt.ai/chat")] // Not a web scheme
    [InlineData("http://voxt.ai:8443/chat")] // An explicit port belongs to a single scheme
    [InlineData("//evil.example")]
    public void TryParseAppLinkShouldRejectEverythingElse(string value)
    {
        // arrange
        var origin = new Uri("https://voxt.ai");

        // act
        var isParsed = LocalUrl.TryParseAppLink(value, origin, out _);

        // assert
        isParsed.Should().BeFalse();
    }

    [Theory]
    [InlineData("https://voxt.ai/chat/x", "/chat/x")]
    [InlineData("https://VOXT.ai/chat/x", "/chat/x")]
    [InlineData("https://voxt.ai", "/")]
    [InlineData("https://voxt.ai/chat?x=1#f", "/chat?x=1#f")]
    public void FromAbsoluteShouldAcceptSameOrigin(string url, string expected)
    {
        // arrange
        var mapper = new UrlMapper("https://voxt.ai/");

        // act
        var localUrl = LocalUrl.FromAbsolute(url, mapper);

        // assert
        localUrl.Should().NotBeNull();
        localUrl!.Value.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://voxt.ai.evil.example/chat")] // Shares the origin's prefix
    [InlineData("https://evil.example/chat")]
    [InlineData("http://voxt.ai/chat")] // Not an app link: only TryParseAppLink relaxes the scheme
    [InlineData("/chat")] // Absolute-only by contract - LinkPreviewUI feeds it raw message markup
    [InlineData("chat")]
    public void FromAbsoluteShouldRejectAnythingButSameOrigin(string url)
    {
        // arrange
        var mapper = new UrlMapper("https://voxt.ai/");

        // act
        var localUrl = LocalUrl.FromAbsolute(url, mapper);

        // assert
        localUrl.Should().BeNull();
    }
}
