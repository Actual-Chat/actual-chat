using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public sealed class UserAttributionsTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IUserAttributionsBackend Backend => AppHost.Services.GetRequiredService<IUserAttributionsBackend>();

    [Fact(Timeout = 60_000)]
    public async Task FlagsShouldFollowFirstAndMostRecentAttribution()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var userId = account.Id;
        var now = Clocks.SystemClock.Now;
        var first = "utm_source=google&utm_campaign=a&utm_term=walkie&gclid=g1";
        var second = "utm_source=google&utm_campaign=b&utm_term=radio";

        // act
        var added1 = await Add(userId, first, now);
        var list1 = await Backend.List(userId, default);

        // assert
        added1.Should().NotBeNull();
        list1.Should().ContainSingle()
            .Which.Should().Match<UserAttribution>(a => a.IsInitial && a.IsLatest, "a single one is both");

        // act
        await Add(userId, second, now + TimeSpan.FromMinutes(1));
        var list2 = await Backend.List(userId, default);

        // assert
        list2.Should().HaveCount(2);
        list2.Single(a => a.Details.Campaign == "a").Should().Match<UserAttribution>(a => a.IsInitial && !a.IsLatest);
        list2.Single(a => a.Details.Campaign == "b").Should().Match<UserAttribution>(a => !a.IsInitial && a.IsLatest);

        // act - the first keyword is clicked again
        await Add(userId, first.Replace("gclid=g1", "gclid=g2"), now + TimeSpan.FromMinutes(2));
        var list3 = await Backend.List(userId, default);

        // assert
        list3.Should().HaveCount(2, "the same attribution seen again is not stored twice");
        var again = list3.Single(a => a.Details.Campaign == "a");
        again.IsInitial.Should().BeTrue();
        again.IsLatest.Should().BeTrue();
        again.Details.ClickId.Should().Be("g2");
        list3.Single(a => a.Details.Campaign == "b").Should().Match<UserAttribution>(a => !a.IsInitial && !a.IsLatest);
    }

    [Fact(Timeout = 60_000)]
    public async Task OlderBackfillShouldBecomeInitial()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var userId = account.Id;
        var now = Clocks.SystemClock.Now;
        await Add(userId, "utm_campaign=recent", now);

        // act
        var backfillCmd = new UserAttributionsBackend_Add(
            userId, AttributionOrigin.LegacySignUp, "campaign:asa-42", now - TimeSpan.FromDays(30));
        await Commander.Call(backfillCmd);
        var list = await Backend.List(userId, default);

        // assert
        list.Should().HaveCount(2);
        list.Single(a => a.Details.Campaign == "42").Should().Match<UserAttribution>(a => a.IsInitial && !a.IsLatest);
        list.Single(a => a.Details.Campaign == "recent")
            .Should().Match<UserAttribution>(a => !a.IsInitial && a.IsLatest);
    }

    [Fact(Timeout = 60_000)]
    public async Task UnattributedValueShouldStoreNothing()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();

        // act
        var added = await Add(account.Id, "utm_source=google-play&utm_medium=organic", Clocks.SystemClock.Now);
        var list = await Backend.List(account.Id, default);

        // assert
        added.Should().BeNull();
        list.Should().BeEmpty();
    }

    [Fact(Timeout = 60_000)]
    public async Task ClientAttributionShouldBeRecordedForSignedInUserOnly()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var now = Clocks.SystemClock.Now;
        var command = new Usage_AddAttribution {
            Session = tester.Session,
            Origin = AttributionOrigin.Url,
            Raw = "/?utm_campaign=landing",
            SeenAt = now + TimeSpan.FromDays(3),
        };

        // act
        var guestCall = async () => await tester.Commander.Call(command);

        // assert
        await guestCall.Should().ThrowAsync<Exception>("a guest's attributions wait on the device");

        // act
        var account = await tester.SignInAsUniqueAlice();
        await tester.Commander.Call(command);
        var list = await Backend.List(account.Id, default);

        // assert
        var attribution = list.Should().ContainSingle().Subject;
        attribution.Details.Campaign.Should().Be("landing");
        attribution.FirstSeenAt.Should().BeLessThanOrEqualTo(Clocks.SystemClock.Now, "a device clock ahead is clamped");
    }

    // Private methods

    private Task<UserAttribution?> Add(UserId userId, string raw, Moment seenAt)
    {
        var addCmd = new UserAttributionsBackend_Add(userId, AttributionOrigin.PlayReferrer, raw, seenAt);
        return Commander.Call(addCmd);
    }
}
