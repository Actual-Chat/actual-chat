using ActualChat.Users.Email;
using Microsoft.AspNetCore.DataProtection;

namespace ActualChat.Users.UnitTests;

public class DigestUnsubscribeTokensTest
{
    [Fact]
    public void ShouldRoundTripUserId()
    {
        // arrange
        var tokens = NewTokens();
        var userId = UserId.New();

        // act
        var token = tokens.Create(userId);
        var parsed = tokens.TryParse(token);

        // assert
        token.Should().NotContain("/", "the token is a URL path segment");
        parsed.Should().Be(userId);
    }

    [Fact]
    public void ShouldRejectGarbage()
    {
        // arrange
        var tokens = NewTokens();

        // act
        var parsedEmpty = tokens.TryParse("");
        var parsedGarbage = tokens.TryParse("not-a-token");

        // assert
        parsedEmpty.Should().BeNull();
        parsedGarbage.Should().BeNull();
    }

    [Fact]
    public void ShouldRejectTokenFromAnotherKeyRing()
    {
        // arrange
        var tokens = NewTokens();
        var otherTokens = NewTokens();

        // act
        var token = otherTokens.Create(UserId.New());
        var parsed = tokens.TryParse(token);

        // assert
        parsed.Should().BeNull();
    }

    private static DigestUnsubscribeTokens NewTokens()
    {
        var services = new ServiceCollection()
            .AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider())
            .BuildServiceProvider();
        return new DigestUnsubscribeTokens(services);
    }
}
