using ActualChat.Chat;

namespace ActualChat.Chat.UnitTests;

public class WebHookTokensTest
{
    [Fact]
    public void NewTokenShouldBePrefixedAndUnique()
    {
        // act
        var a = WebHookTokens.New();
        var b = WebHookTokens.New();

        // assert
        a.Should().StartWith(Constants.WebHooks.TokenPrefix);
        a.Should().NotBe(b);
        a.Length.Should().Be(Constants.WebHooks.TokenPrefix.Length + 43, "32 random bytes in unpadded base64url");
        WebHookTokens.LooksValid(a).Should().BeTrue();
    }

    [Fact]
    public void HashShouldBeStableAndOpaque()
    {
        // arrange
        var token = WebHookTokens.New();

        // act
        var h1 = WebHookTokens.Hash(token);
        var h2 = WebHookTokens.Hash(token);

        // assert
        h1.Should().Be(h2);
        h1.Should().NotContain(token[Constants.WebHooks.TokenPrefix.Length..]);
        h1.Should().MatchRegex("^[A-Za-z0-9_-]{43}$", "SHA-256 as base64url without padding");
    }

    [Theory]
    [InlineData("")]
    [InlineData("whsec_abc")]
    [InlineData("whin_")]
    [InlineData("whin_has spaces")]
    // The secret part is exactly 43 base64url characters, so anything shorter or longer is garbage
    [InlineData("whin_000000000000000000000000000000000000000000")] // 42 chars, one short
    [InlineData("whin_00000000000000000000000000000000000000000000")] // 44 chars, one over
    [InlineData("whin_000000000000000000000000000000000000000000+")] // 43 chars, but '+' is not base64url
    public void LooksValidShouldRejectGarbage(string token)
        => WebHookTokens.LooksValid(token).Should().BeFalse();
}
