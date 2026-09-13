namespace ActualChat.Users.UnitTests.AppUpdates;

public sealed class StoreProbeTest
{
    [Fact]
    public async Task AppleProbeShouldReadTheBuildVersionAndReleaseDate()
    {
        // arrange
        var body = (await ReadFixture("apple-lookup-us.json")).Replace("\"2.17\"", "\"2.19.40\"");

        // act
        var result = AppStoreProbes.ParseApple(body);

        // assert
        result.Should().NotBeNull();
        result!.VersionString.Should().Be("2.19.40");
        result.Version.Should().Be(new Version(2, 19, 40));
        result.ReleasedAt.Should().Be(new Moment(DateTimeOffset.Parse("2026-08-31T01:20:07Z")));
    }

    [Fact]
    public async Task AppleProbeShouldReadAMarketingOnlyVersionAsTheLastOfItsTrain()
    {
        // arrange - "2.17" is what the App Store showed before releases moved to the build version
        var body = await ReadFixture("apple-lookup-us.json");

        // act
        var result = AppStoreProbes.ParseApple(body);

        // assert
        result.Version.Should().Be(new Version(2, 17, 9999),
            "the build is unknown, and the last of the train can't hide a real update");
        result.VersionString.Should().Be("2.17.9999");
    }

    [Fact]
    public async Task AppleProbeShouldThrowOnAnUnlistedStorefront()
    {
        // arrange
        var body = await ReadFixture("apple-lookup-empty.json");

        // act
        var parse = () => AppStoreProbes.ParseApple(body);

        // assert
        parse.Should().Throw<Exception>();
    }

    [Fact]
    public void AppleProbeShouldThrowOnAnUnreadableResponse()
    {
        // act
        var noResults = () => AppStoreProbes.ParseApple("{}");
        var noVersion = () => AppStoreProbes.ParseApple(
            """{"resultCount":1,"results":[{"kind":"software"}]}""");

        // assert
        noResults.Should().Throw<Exception>();
        noVersion.Should().Throw<Exception>();
    }

    [Fact]
    public void AppleProbeUriShouldTargetTheAppInTheUsStorefront()
    {
        // act
        var uri = new Uri(string.Format(AppStoreProbes.AppleUriFormat, "chat.actual.app"));

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
        var result = AppStoreProbes.ParseGoogle(body);

        // assert
        result.Should().NotBeNull();
        result!.VersionString.Should().Be("2.17.246");
        result.Version.Should().Be(new Version(2, 17, 246));
        result.ReleasedAt.Should().BeNull();
        body.Should().Contain("\"1.6.46\"", "the fixture must keep a decoy version to be a real test");
    }

    [Fact]
    public async Task GoogleProbeShouldThrowWhenTheVersionBlockCountIsNotOne()
    {
        // arrange
        var body = await ReadFixture("google-play-page.html");

        // act
        var none = () => AppStoreProbes.ParseGoogle("<html><body>no version here</body></html>");
        var many = () => AppStoreProbes.ParseGoogle(body + body);

        // assert
        none.Should().Throw<Exception>();
        many.Should().Throw<Exception>();
    }

    [Fact]
    public void GoogleProbeUriShouldTargetTheAppInTheUsStorefront()
    {
        // act
        var uri = new Uri(string.Format(AppStoreProbes.GoogleUriFormat, "chat.actual.app"));

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
        var result = AppStoreProbes.ParseMicrosoft(body);

        // assert
        result.Should().NotBeNull();
        // The store shows "2.17.246.0"; VersionString is that normalized to the build version
        result!.VersionString.Should().Be("2.17.246");
        result.Version.Should().Be(new Version(2, 17, 246));
        result.ReleasedAt.Should().Be(new Moment(DateTimeOffset.Parse("2026-08-28T21:39:37.1310088Z")));
        body.Should().Contain("2.16.608.0", "the fixture must keep the older package to prove max() is used");
    }

    [Fact]
    public async Task MicrosoftProbeShouldThrowOnAnUnlistedMarket()
    {
        // arrange
        var body = await ReadFixture("microsoft-displaycatalog-empty.json");

        // act
        var parse = () => AppStoreProbes.ParseMicrosoft(body);

        // assert
        parse.Should().Throw<Exception>();
    }

    [Fact]
    public void MicrosoftProbeShouldThrowOnAnUnreadableResponse()
    {
        // act
        var noProducts = () => AppStoreProbes.ParseMicrosoft("{}");
        var noPackages = () => AppStoreProbes.ParseMicrosoft(
            """{"Products":[{"ProductId":"9N6RWRD9FMS2","DisplaySkuAvailabilities":[]}]}""");

        // assert
        noProducts.Should().Throw<Exception>();
        noPackages.Should().Throw<Exception>();
    }

    [Fact]
    public void MicrosoftProbeUriShouldTargetTheAppInTheUsMarket()
    {
        // act
        var uri = new Uri(string.Format(AppStoreProbes.MicrosoftUriFormat, "9N6RWRD9FMS2"));

        // assert
        uri.Host.Should().Be("displaycatalog.mp.microsoft.com");
        uri.Query.Should().Contain("bigIds=9N6RWRD9FMS2").And.Contain("market=US");
    }

    [Fact]
    public void CacheBusterShouldDifferPerCallAndKeepTheOriginalQuery()
    {
        // arrange
        var uri = new Uri(string.Format(AppStoreProbes.AppleUriFormat, "chat.actual.app"));

        // act
        var first = AppStoreProbes.AddCacheBuster(uri);
        var second = AppStoreProbes.AddCacheBuster(uri);

        // assert
        first.Query.Should().Contain("bundleId=chat.actual.app").And.Contain("country=us");
        first.Should().NotBe(second);
        new Uri(first.GetLeftPart(UriPartial.Path)).Should().Be(new Uri(uri.GetLeftPart(UriPartial.Path)));
    }

    // Private methods

    private static Task<string> ReadFixture(string name)
        => File.ReadAllTextAsync($"AppUpdates/Fixtures/{name}");
}
