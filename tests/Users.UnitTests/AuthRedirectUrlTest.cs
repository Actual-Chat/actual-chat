namespace ActualChat.Users.UnitTests;

public class AuthRedirectUrlTest
{
    [Theory]
    [InlineData("/chat", "/chat")] // Relative
    [InlineData("/chat?x=1&y=2", "/chat?x=1&y=2")] // Relative with query
    [InlineData("/chat?x=1#f", "/chat?x=1#f")] // Relative with query and fragment
    [InlineData("voxt://auth-complete", "voxt://auth-complete")] // Prod app scheme
    [InlineData("voxt-dev://auth-complete", "voxt-dev://auth-complete")] // Dev app scheme
    [InlineData("VOXT://auth-complete", "VOXT://auth-complete")] // App scheme, case-insensitive
    public void KeepsWhatCannotBeReduced(string redirectUrl, string expected)
    {
        // act
        var result = AuthRedirectUrl.Sanitize(redirectUrl);

        // assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://voxt.ai/chat", "/chat")] // Own host
    [InlineData("https://ACTUAL.CHAT/chat", "/chat")] // Own host, case-insensitive
    [InlineData("https://voxt.ai/chat?x=1#f", "/chat?x=1#f")] // Query and fragment survive
    [InlineData("https://evil.com/x", "/x")] // Foreign host: the path lands on this server instead
    [InlineData("https://voxt.ai.evil.com/x", "/x")] // Suffix-lookalike host, same treatment
    [InlineData("https://wt1.local.voxt.ai/chat", "/chat")] // Worktree deployment
    [InlineData("chat", "/chat")] // Relative but not rooted: normalized rather than rejected
    [InlineData("/chat/", "/chat")] // Trailing slash normalized away
    public void ReducesAbsoluteUrlsToTheirPath(string redirectUrl, string expected)
    {
        // act
        var result = AuthRedirectUrl.Sanitize(redirectUrl);

        // assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("//evil.com/x")] // Protocol-relative
    [InlineData("/\\evil.com/x")] // Backslash protocol-relative
    [InlineData("javascript:alert(1)")] // Script scheme
    [InlineData("data:text/html,<script>")] // Data scheme
    [InlineData("/\t/evil.com")] // Embedded tab: browsers strip it, collapsing to "//evil.com"
    [InlineData("/\n/evil.com")] // Embedded newline: same collapse
    [InlineData("/\r/evil.com")] // Embedded carriage return: same collapse
    [InlineData("https://voxt.ai/\t/evil.com")] // Embedded tab in an otherwise-reducible absolute URL
    [InlineData("voxt://something-else")] // App scheme, but not the auth callback host
    [InlineData("")]
    [InlineData(null)]
    public void RejectsWhatCannotBecomeALocalUrl(string? redirectUrl)
    {
        // act
        var result = AuthRedirectUrl.Sanitize(redirectUrl);

        // assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("voxt://auth-complete", true)] // Prod app scheme
    [InlineData("voxt-dev://auth-complete", true)] // Dev app scheme
    [InlineData("VOXT://auth-complete", true)] // App scheme, case-insensitive
    [InlineData("voxt://something-else", true)] // App scheme, regardless of host
    [InlineData("https://voxt.ai/chat", false)] // Own host, but not an app scheme
    [InlineData("/chat", false)] // Relative
    [InlineData("javascript:alert(1)", false)] // Script scheme
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAppSchemeShouldDetectAppSchemeUrls(string? url, bool expected)
    {
        // act
        var result = AuthRedirectUrl.IsAppScheme(url);

        // assert
        result.Should().Be(expected);
    }
}
