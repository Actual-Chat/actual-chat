using ActualChat.Testing.Host;
using ActualLab.Versioning;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class UserVoicesBackendTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task CreateThenGetShouldReturnTheRecord()
    {
        // arrange
        var appHost = AppHost;
        var backend = appHost.Services.GetRequiredService<IUserVoicesBackend>();
        await using var tester = appHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var now = Clocks.SystemClock.Now;
        var diff = new UserVoiceDiff {
            SonioxVoiceId = "voice-1",
            Status = UserVoiceStatus.Creating,
            LastUsedAt = now,
            CreatedAt = now,
            ModifiedAt = now,
        };

        // act
        var created = await tester.Commander.Call(new UserVoicesBackend_Change(account.Id, null, Change.Create(diff)));
        var fetched = await backend.Get(account.Id, default);

        // assert
        created.Should().NotBeNull();
        fetched.Should().NotBeNull();
        fetched!.UserId.Should().Be(account.Id);
        fetched.SonioxVoiceId.Should().Be("voice-1");
        fetched.Status.Should().Be(UserVoiceStatus.Creating);
        fetched.Version.Should().Be(created!.Version);
    }

    [Fact]
    public async Task UpdateWithWrongExpectedVersionShouldThrow()
    {
        // arrange
        var appHost = AppHost;
        await using var tester = appHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var now = Clocks.SystemClock.Now;
        var createDiff = new UserVoiceDiff {
            Status = UserVoiceStatus.Creating,
            LastUsedAt = now,
            CreatedAt = now,
            ModifiedAt = now,
        };
        var created = await tester.Commander.Call(new UserVoicesBackend_Change(account.Id, null, Change.Create(createDiff)));
        var updateDiff = new UserVoiceDiff { Status = UserVoiceStatus.Ready };

        // act
        var act = () => tester.Commander.Call(
            new UserVoicesBackend_Change(account.Id, created!.Version + 1, Change.Update(updateDiff)));

        // assert
        await act.Should().ThrowAsync<VersionMismatchException>();
    }

    [Fact]
    public async Task ListActiveShouldReturnOnlyCreatingOrReady()
    {
        // arrange
        var appHost = AppHost;
        var backend = appHost.Services.GetRequiredService<IUserVoicesBackend>();
        var now = Clocks.SystemClock.Now;

        await using var creatingTester = appHost.NewWebClientTester(Out);
        var creatingAccount = await creatingTester.SignInAsUniqueAlice();
        await creatingTester.Commander.Call(new UserVoicesBackend_Change(creatingAccount.Id, null,
            Change.Create(new UserVoiceDiff { Status = UserVoiceStatus.Creating, LastUsedAt = now, CreatedAt = now, ModifiedAt = now })));

        await using var readyTester = appHost.NewWebClientTester(Out);
        var readyAccount = await readyTester.SignInAsUniqueAlice();
        await readyTester.Commander.Call(new UserVoicesBackend_Change(readyAccount.Id, null,
            Change.Create(new UserVoiceDiff { Status = UserVoiceStatus.Ready, LastUsedAt = now, CreatedAt = now, ModifiedAt = now })));

        await using var failedTester = appHost.NewWebClientTester(Out);
        var failedAccount = await failedTester.SignInAsUniqueAlice();
        await failedTester.Commander.Call(new UserVoicesBackend_Change(failedAccount.Id, null,
            Change.Create(new UserVoiceDiff { Status = UserVoiceStatus.Failed, LastUsedAt = now, CreatedAt = now, ModifiedAt = now })));

        // act
        var active = await backend.ListActive(default);
        var activeUserIds = active.Select(x => x.UserId).ToList();

        // assert
        activeUserIds.Should().Contain(creatingAccount.Id);
        activeUserIds.Should().Contain(readyAccount.Id);
        activeUserIds.Should().NotContain(failedAccount.Id);
    }
}
