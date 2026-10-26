namespace ActualChat.Users.UnitTests.Usage;

public sealed class AttributionParserTest
{
    [Fact]
    public void PlayReferrerWithValueTrackShouldKeepTermAndMatchType()
    {
        // arrange
        var raw = "utm_source=google&utm_medium=cpc&utm_campaign=exp-walkie-gads"
            + "&utm_term=walkie%20talkie%20app&utm_content=p&gclid=Cj0KCQ";

        // act
        var details = AttributionParser.Parse(AttributionOrigin.PlayReferrer, raw);

        // assert
        details.Should().NotBeNull();
        details!.Platform.Should().Be("google");
        details.Source.Should().Be("google");
        details.Medium.Should().Be("cpc");
        details.Campaign.Should().Be("exp-walkie-gads");
        details.Term.Should().Be("walkie talkie app");
        details.MatchType.Should().Be("p");
        details.AdGroup.Should().BeEmpty("utm_content held the match type");
        details.ClickIdKind.Should().Be(ClickIdKind.Gclid);
        details.ClickId.Should().Be("Cj0KCQ");
    }

    [Fact]
    public void UrlShouldAcceptShortCampaignAndAdGroup()
    {
        // act
        var details = AttributionParser.Parse(AttributionOrigin.Url, "/chat?c=spring&utm_content=group-1#x");

        // assert
        details.Should().NotBeNull();
        details!.Platform.Should().Be("web");
        details.Campaign.Should().Be("spring");
        details.AdGroup.Should().Be("group-1");
        details.MatchType.Should().BeEmpty();
    }

    [Fact]
    public void ClickIdAloneShouldBeAnAttribution()
    {
        // act
        var details = AttributionParser.Parse(AttributionOrigin.Url, "/?gbraid=0AAAA");

        // assert
        details.Should().NotBeNull();
        details!.Platform.Should().Be("google");
        details.ClickIdKind.Should().Be(ClickIdKind.Gbraid);
        details.Campaign.Should().BeEmpty();
    }

    [Fact]
    public void UnknownOriginShouldYieldNothing()
    {
        // act
        var details = AttributionParser.Parse(AttributionOrigin.Unknown, "/?c=spring&gclid=G1");

        // assert
        details.Should().BeNull();
    }

    [Theory]
    [InlineData("utm_source=google-play&utm_medium=organic")]
    [InlineData("no-query-here")]
    [InlineData("")]
    [InlineData(null)]
    public void UnattributedReferrerShouldYieldNull(string? raw)
    {
        // act
        var details = AttributionParser.Parse(AttributionOrigin.PlayReferrer, raw);

        // assert
        details.Should().BeNull();
    }

    [Fact]
    public void AppleAnswerShouldKeepEveryId()
    {
        // arrange
        var raw = """
            {"attribution":true,"orgId":40669820,"campaignId":542370539,"conversionType":"Download",
            "claimType":"Click","adGroupId":542317095,"countryOrRegion":"US","keywordId":87675432,"adId":542317136}
            """;

        // act
        var details = AttributionParser.Parse(AttributionOrigin.AppleAdServices, raw);

        // assert
        details.Should().NotBeNull();
        details!.Platform.Should().Be("apple");
        details.Campaign.Should().Be("542370539");
        details.AdGroup.Should().Be("542317095");
        details.KeywordId.Should().Be("87675432");
        details.AdId.Should().Be("542317136");
        details.Claim.Should().Be("Click");
        details.InstallKind.Should().Be("Download");
        details.Country.Should().Be("US");
    }

    [Theory]
    [InlineData("""{"attribution":false,"campaignId":1}""")]
    [InlineData("""{"attribution":"true","campaignId":1}""")]
    [InlineData("[1,2]")]
    [InlineData("{not json")]
    public void UnattributedOrMalformedAppleAnswerShouldYieldNull(string raw)
    {
        // act
        var details = AttributionParser.Parse(AttributionOrigin.AppleAdServices, raw);

        // assert
        details.Should().BeNull();
    }

    [Theory]
    [InlineData("join:inv-1", "referral", "join", "inv-1")]
    [InlineData("user:hjp639qb6bp1", "referral", "user", "hjp639qb6bp1")]
    [InlineData("campaign:asa-542370539", "apple", "apple_ads", "542370539")]
    [InlineData("campaign:spring", "", "", "spring")]
    public void ArrivalShouldMapToDetails(string raw, string platform, string source, string campaign)
    {
        // act
        var details = AttributionParser.Parse(AttributionOrigin.LegacySignUp, raw);

        // assert
        details.Should().NotBeNull();
        details!.Platform.Should().Be(platform);
        details.Source.Should().Be(source);
        details.Campaign.Should().Be(campaign);
    }

    [Theory]
    [InlineData("store")]
    [InlineData("web")]
    [InlineData("bogus:1")]
    public void UnattributedArrivalShouldYieldNull(string raw)
    {
        // act
        var details = AttributionParser.Parse(AttributionOrigin.Referral, raw);

        // assert
        details.Should().BeNull();
    }

    [Fact]
    public void HostileValuesShouldBeCleanedAndCapped()
    {
        // arrange
        var longTerm = new string('t', 300);
        var raw = $"utm_campaign=a%0Ab%00c&utm_term={longTerm}&utm_source={new string('s', 100)}";

        // act
        var details = AttributionParser.Parse(AttributionOrigin.Url, raw);

        // assert
        details.Should().NotBeNull();
        details!.Campaign.Should().Be("abc", "control characters are stripped");
        details.Term.Length.Should().Be(AttributionParser.MaxTermLength);
        details.Source.Length.Should().Be(AttributionParser.MaxValueLength);
    }

    [Fact]
    public void OversizedRawShouldYieldNull()
    {
        // arrange
        var raw = "utm_campaign=x&pad=" + new string('p', AttributionParser.MaxRawLength);

        // act
        var details = AttributionParser.Parse(AttributionOrigin.Url, raw);

        // assert
        details.Should().BeNull();
    }

    [Fact]
    public void DedupeKeyShouldPreferKeywordIdOverTerm()
    {
        // arrange
        var details = new AttributionDetails {
            Platform = "apple", Campaign = "1", AdGroup = "2", KeywordId = "3", Term = "ignored",
        };

        // act
        var key = details.DedupeKey;

        // assert
        key.Should().Be("apple|1|2|3");
    }
}
