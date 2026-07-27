using ActualChat.Hosting;

namespace ActualChat.Users.UnitTests;

public class AuthRedirectUrlTest
{
    private static readonly IReadOnlySet<string> AllowedHosts
        = new HashSet<string>(["voxt.ai", "actual.chat"], StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("/chat", true)] // Relative
    [InlineData("/chat?x=1&y=2", true)] // Relative with query
    [InlineData("https://voxt.ai/chat", true)] // Allowed host
    [InlineData("https://ACTUAL.CHAT/chat", true)] // Allowed host, case-insensitive
    [InlineData("voxt://auth-complete", true)] // Prod app scheme
    [InlineData("voxt-dev://auth-complete", true)] // Dev app scheme
    [InlineData("VOXT://auth-complete", true)] // App scheme, case-insensitive
    [InlineData("https://evil.com/x", false)] // Foreign host
    [InlineData("https://voxt.ai.evil.com/x", false)] // Suffix-lookalike host
    [InlineData("//evil.com/x", false)] // Protocol-relative
    [InlineData("/\\evil.com/x", false)] // Backslash protocol-relative
    [InlineData("javascript:alert(1)", false)] // Script scheme
    [InlineData("data:text/html,<script>", false)] // Data scheme
    [InlineData("chat", false)] // Relative but not rooted
    [InlineData("/\t/evil.com", false)] // Embedded tab: browsers strip it, collapsing to "//evil.com"
    [InlineData("/\n/evil.com", false)] // Embedded newline: same collapse
    [InlineData("/\r/evil.com", false)] // Embedded carriage return: same collapse
    [InlineData("https://voxt.ai/\t/evil.com", false)] // Embedded tab in an otherwise-allowed absolute URL
    [InlineData("voxt://something-else", false)] // App scheme, but not the auth callback host
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ShouldAllowOnlySafeRedirects(string? redirectUrl, bool isAllowed)
    {
        // act
        var result = AuthRedirectUrl.Sanitize(redirectUrl, AllowedHosts);

        // assert
        if (isAllowed)
            result.Should().Be(redirectUrl);
        else
            result.Should().BeNull();
    }

    [Fact]
    public void ShouldAllowWorktreeHostOfOwnDeployment()
    {
        // arrange
        var hostInfo = new HostInfo { BaseUrl = "https://wt1.local.voxt.ai/" };
        var allowedHosts = hostInfo.GetOwnAndKnownHosts();

        // act
        var result = AuthRedirectUrl.Sanitize("https://wt1.local.voxt.ai/chat", allowedHosts);

        // assert
        hostInfo.BaseUrlKind.Should().Be(BaseUrlKind.Local);
        hostInfo.GetHosts().Should().NotContain("wt1.local.voxt.ai");
        result.Should().Be("https://wt1.local.voxt.ai/chat");
    }

    [Fact]
    public void ShouldRejectForeignHostOnWorktreeDeployment()
    {
        // arrange
        var allowedHosts = new HostInfo { BaseUrl = "https://wt1.local.voxt.ai/" }.GetOwnAndKnownHosts();

        // act
        var result = AuthRedirectUrl.Sanitize("https://wt2.local.voxt.ai/chat", allowedHosts);

        // assert
        result.Should().BeNull();
    }
}
