using ActualChat.Users.AppStores;

namespace ActualChat.Users.UnitTests.AppUpdates;

public sealed class StoreProbeTest
{
    [Fact]
    public async Task AppleProbeShouldReadTheMarketingVersionAndReleaseDate()
    {
        // arrange
        var body = await ReadFixture("apple-lookup-us.json");

        // act
        var result = AppleStoreProbe.Parse(body);

        // assert
        result.Should().NotBeNull();
        result!.Value.StoreVersion.Should().Be("2.17");
        // Two components, so it's a marketing train rather than a build version
        result.Value.BuildVersion.Should().BeNull();
        result.Value.ReleasedAt.Should().Be(new Moment(DateTimeOffset.Parse("2026-08-31T01:20:07Z")));
    }

    [Fact]
    public async Task AppleProbeShouldReadAThreePartVersionAsTheBuildVersion()
    {
        // arrange
        var body = (await ReadFixture("apple-lookup-us.json")).Replace("\"2.17\"", "\"2.19.40\"");

        // act
        var result = AppleStoreProbe.Parse(body);

        // assert
        result!.Value.BuildVersion.Should().Be(new Version(2, 19, 40));
        result.Value.StoreVersion.Should().Be("2.19.40");
    }

    [Fact]
    public async Task AppleProbeShouldReportAnUnlistedStorefrontAsNull()
    {
        // arrange
        var body = await ReadFixture("apple-lookup-empty.json");

        // act
        var result = AppleStoreProbe.Parse(body);

        // assert
        result.Should().BeNull();
    }

    [Fact]
    public void AppleProbeShouldThrowOnAnUnreadableResponse()
    {
        // act
        var noResults = () => AppleStoreProbe.Parse("{}");
        var noVersion = () => AppleStoreProbe.Parse(
            """{"resultCount":1,"results":[{"kind":"software"}]}""");

        // assert
        noResults.Should().Throw<Exception>();
        noVersion.Should().Throw<Exception>();
    }

    [Fact]
    public void AppleProbeUriShouldTargetTheAppInTheUsStorefront()
    {
        // act
        var uri = AppleStoreProbe.GetUri("chat.actual.app");

        // assert
        uri.Host.Should().Be("itunes.apple.com");
        uri.Query.Should().Contain("bundleId=chat.actual.app").And.Contain("country=us");
    }

    [Fact]
    public async Task GoogleProbeShouldReadTheVersionBlockAndIgnoreReviewMetadata()
    {
        // arrange
        var body = await ReadFixture("google-play-page.html");

        // act
        var result = GoogleStoreProbe.Parse(body);

        // assert
        result.StoreVersion.Should().Be("2.17.246");
        result.BuildVersion.Should().Be(new Version(2, 17, 246));
        result.ReleasedAt.Should().BeNull();
        body.Should().Contain("\"1.6.46\"", "the fixture must keep a decoy version to be a real test");
    }

    [Fact]
    public async Task GoogleProbeShouldThrowWhenTheVersionBlockCountIsNotOne()
    {
        // arrange
        var body = await ReadFixture("google-play-page.html");

        // act
        var none = () => GoogleStoreProbe.Parse("<html><body>no version here</body></html>");
        var many = () => GoogleStoreProbe.Parse(body + body);

        // assert
        none.Should().Throw<Exception>();
        many.Should().Throw<Exception>();
    }

    [Fact]
    public void GoogleProbeUriShouldTargetTheAppInTheUsStorefront()
    {
        // act
        var uri = GoogleStoreProbe.GetUri("chat.actual.app");

        // assert
        uri.Host.Should().Be("play.google.com");
        uri.Query.Should().Contain("id=chat.actual.app").And.Contain("gl=US");
    }

    [Fact]
    public async Task MicrosoftProbeShouldTakeTheHighestPackageVersion()
    {
        // arrange
        var body = await ReadFixture("microsoft-displaycatalog.json");

        // act
        var result = MicrosoftStoreProbe.Parse(body);

        // assert
        result.Should().NotBeNull();
        result!.Value.StoreVersion.Should().Be("2.17.246.0");
        result.Value.BuildVersion.Should().Be(new Version(2, 17, 246));
        result.Value.ReleasedAt.Should().Be(new Moment(DateTimeOffset.Parse("2026-08-28T21:39:37.1310088Z")));
        body.Should().Contain("2.16.608.0", "the fixture must keep the older package to prove max() is used");
    }

    [Fact]
    public async Task MicrosoftProbeShouldReportAnUnlistedMarketAsNull()
    {
        // arrange
        var body = await ReadFixture("microsoft-displaycatalog-empty.json");

        // act
        var result = MicrosoftStoreProbe.Parse(body);

        // assert
        result.Should().BeNull();
    }

    [Fact]
    public void MicrosoftProbeShouldThrowOnAnUnreadableResponse()
    {
        // act
        var noProducts = () => MicrosoftStoreProbe.Parse("{}");
        var noPackages = () => MicrosoftStoreProbe.Parse(
            """{"Products":[{"ProductId":"9N6RWRD9FMS2","DisplaySkuAvailabilities":[]}]}""");

        // assert
        noProducts.Should().Throw<Exception>();
        noPackages.Should().Throw<Exception>();
    }

    [Fact]
    public void MicrosoftProbeUriShouldTargetTheAppInTheUsMarket()
    {
        // act
        var uri = MicrosoftStoreProbe.GetUri("9N6RWRD9FMS2");

        // assert
        uri.Host.Should().Be("displaycatalog.mp.microsoft.com");
        uri.Query.Should().Contain("bigIds=9N6RWRD9FMS2").And.Contain("market=US");
    }

    // Private methods

    private static Task<string> ReadFixture(string name)
        => File.ReadAllTextAsync($"AppUpdates/Fixtures/{name}");
}
