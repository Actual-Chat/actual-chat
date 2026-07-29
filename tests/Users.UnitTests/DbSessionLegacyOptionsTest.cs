using ActualChat.Users.Db;

namespace ActualChat.Users.UnitTests;

public class DbSessionLegacyOptionsTest(ITestOutputHelper @out) : TestBase(@out)
{
    // Captured from ImmutableOptionSet, which is how every _Sessions row written before 2026-07 looks
    private const string LegacyOptionsJson =
        """
        {"Items":{"@ActualChat_Users_GuestIdOption":
        {"$type":"ActualChat.Users.GuestIdOption, ActualChat.Api","GuestId":"~O9XHJbSL"}}}
        """;

    [Fact]
    public void KeepsGuestIdFromLegacyOptions()
    {
        // arrange
        var dbSession = new DbSession();

        // act
        dbSession.OptionsJson = LegacyOptionsJson;

        // assert
        dbSession.Options["GuestId"].Should().Be("~O9XHJbSL");
    }

    [Fact]
    public void RewritesLegacyOptionsWithoutTypeNames()
    {
        // arrange
        var dbSession = new DbSession { OptionsJson = LegacyOptionsJson };

        // act
        var json = dbSession.OptionsJson;

        // assert
        json.Should().Contain("GuestId").And.Contain("~O9XHJbSL");
        json.Should().NotContain("$type");
    }

    [Fact]
    public void RoundTripsCurrentOptions()
    {
        // arrange
        var guestId = UserId.NewGuest();
        var source = new DbSession { Options = MetadataBag.Empty.Set("GuestId", guestId.Value) };

        // act
        var dbSession = new DbSession { OptionsJson = source.OptionsJson };

        // assert
        dbSession.Options["GuestId"].Should().Be(guestId.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"Items":[]}""")]
    [InlineData("""{"Items":{"GuestId":"not-a-guest"}}""")]
    public void SurvivesUnusableOptions(string optionsJson)
    {
        // arrange
        var dbSession = new DbSession();

        // act
        var act = () => dbSession.OptionsJson = optionsJson;

        // assert
        act.Should().NotThrow();
    }
}
