namespace ActualChat.Users.UnitTests.Usage;

public class ArrivalInfoTest
{
    [Theory]
    [InlineData("web", ArrivalKind.Web, "")]
    [InlineData("store", ArrivalKind.Store, "")]
    [InlineData("join:inv-123_A", ArrivalKind.Join, "inv-123_A")]
    [InlineData("user:hjp639qb6bp1", ArrivalKind.User, "hjp639qb6bp1")]
    [InlineData("campaign:spring.2026@x", ArrivalKind.Campaign, "spring.2026@x")]
    public void ValidValueShouldRoundTrip(string value, ArrivalKind kind, string id)
    {
        // act
        var isParsed = ArrivalInfo.TryParse(value, out var arrival);

        // assert
        isParsed.Should().BeTrue();
        arrival.Kind.Should().Be(kind);
        arrival.Id.Should().Be(id);
        arrival.Format().Should().Be(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("foo")]
    [InlineData("web:x")]
    [InlineData("store:x")]
    [InlineData("join:")]
    [InlineData("join")]
    [InlineData("join:has space")]
    [InlineData("campaign:a/b")]
    [InlineData("JOIN:abc")]
    [InlineData("join:abc\n")]
    public void InvalidValueShouldNotParse(string? value)
    {
        // act
        var isParsed = ArrivalInfo.TryParse(value, out _);

        // assert
        isParsed.Should().BeFalse();
    }

    [Fact]
    public void OverLongIdShouldBeRejected()
    {
        // arrange
        var id = new string('a', ArrivalInfo.MaxIdLength + 1);

        // act
        var arrival = ArrivalInfo.New(ArrivalKind.Campaign, id);
        var isParsed = ArrivalInfo.TryParse("campaign:" + id, out _);

        // assert
        arrival.Should().BeNull();
        isParsed.Should().BeFalse();
        ArrivalInfo.New(ArrivalKind.Campaign, id[..^1]).Should().NotBeNull("the max length itself is allowed");
    }

    [Theory]
    [InlineData("/?utm_source=x&utm_campaign=spring", "campaign:spring")]
    [InlineData("/chats?c=promo1", "campaign:promo1")]
    [InlineData("utm_source=google-play&utm_campaign=play1", "campaign:play1")]
    [InlineData("utm_campaign=asa-542370539", "campaign:asa-542370539")]
    [InlineData("utm_campaign=asa-test", "campaign:asa-test")]
    [InlineData("/?utm_campaign=spring&c=other", "campaign:spring")]
    [InlineData("/?utm_campaign=has%20space", null)]
    [InlineData("/join/abc", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void FromQueryShouldReadTheCampaign(string? urlOrQuery, string? expected)
    {
        // act
        var arrival = ArrivalInfo.FromQuery(urlOrQuery);

        // assert
        arrival?.Format().Should().Be(expected);
        if (expected is null)
            arrival.Should().BeNull();
    }

    [Theory]
    [InlineData(AppKind.Unknown, ArrivalKind.Web)]
    [InlineData(AppKind.Wasm, ArrivalKind.Web)]
    [InlineData(AppKind.Android, ArrivalKind.Store)]
    [InlineData(AppKind.Ios, ArrivalKind.Store)]
    [InlineData(AppKind.Windows, ArrivalKind.Store)]
    public void FallbackShouldDependOnTheApp(AppKind appKind, ArrivalKind expected)
        => ArrivalInfo.Fallback(appKind).Kind.Should().Be(expected);
}
