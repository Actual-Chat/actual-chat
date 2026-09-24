using ActualChat.App.Server.Flows;
using System.Collections.Concurrent;
using ActualChat.Testing.Host;
using ActualChat.Users.Flows;

namespace ActualChat.Users.IntegrationTests.Flows;

public class DigestFlowTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(DigestFlowTest)}", TestAppHostOptions.Default, @out)
{
    [Theory]
    [InlineData(4, 4, 0)]
    [InlineData(5, 5, 0)]
    [InlineData(6, 5, 1)]
    public void OmitsEllipsisAtExactLimit(int totalChatCount, int expectedVisibleCount, int expectedOtherCount)
    {
        var visibleCount = 0;

        // act
        for (var i = 0; i < totalChatCount; i++) {
            if (EmailsBackend.HasUnreadChatCapacity(visibleCount, 5))
                visibleCount++;
        }
        var otherCount = totalChatCount - visibleCount;

        // assert
        visibleCount.Should().Be(expectedVisibleCount);
        otherCount.Should().Be(expectedOtherCount);
    }

    [Fact]
    public async Task MigrationFlow_Should_Start_DigestFlow()
    {
        using var cts = NewTestCts();
        var ct = cts.Token;
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();
        await flowHub.Get<MigrationFlow>("", ct); // We need to manually start it in this test
        var userId = Constants.User.Admin.UserId;

        // MigrationFlow should start AccountMigrationFlow
        await TestWait.When(async innerCt => {
            var flow = await flowHub.TryGet<AccountMigrationFlow>("", innerCt);
            flow.Should().NotBeNull();
        }, TimeSpan.FromSeconds(30));

        // AccountMigrationFlow should start DigestFlow(admin)
        await TestWait.When(async innerCt => {
            var flow = await flowHub.TryGet<DigestFlow>(userId.Value, innerCt);
            flow.Should().NotBeNull();
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldStopFlowIfUserHasNoTimeZone()
    {
        using var cts = NewTestCts();
        var ct = cts.Token;
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();

        var userId = Constants.User.Admin.UserId.Value;
        var f0 = await flowHub.Get<DigestFlow>(userId, ct);

        await TestWait.When(async innerCt => {
            var flow = await flowHub.TryGet<DigestFlow>(f0.Id.Arguments, innerCt);
            flow.Should().NotBeNull();
            flow.LastReadiness.IsSuspended.Should().BeTrue();
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldRunDigestFlowOnTimeZoneUpdate()
    {
        using var cts = NewTestCts();
        var ct = cts.Token;
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();
        var commander = h.Services.Commander();
        var accountsBackend = h.Services.GetRequiredService<IAccountsBackend>();

        var userId = Constants.User.Admin.UserId;
        await flowHub.Get<DigestFlow>(userId.Value, ct);

        var account = await accountsBackend.Get(userId, ct).Require();
        var email = ActualChat.Email.Parse($"admin{Constants.Team.EmailSuffix}");
        var updateCmd = new AccountsBackend_Update(
            account
                .WithEmailIdentity(email) with {
                TimeZone = "America/New_York",
            },
            null);
        await commander.Call(updateCmd, true, ct);

        await TestWait.When(async innerCt => {
            var flow = await flowHub.TryGet<DigestFlow>(userId.Value, innerCt);
            flow.Should().NotBeNull();
            flow.LastReadiness.IsSuspended.Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldWaitTillDigestTime()
    {
        using var cts = NewTestCts();
        var ct = cts.Token;
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();
        var commander = h.Services.Commander();
        var accountsBackend = h.Services.GetRequiredService<IAccountsBackend>();

        var userId = Constants.User.Admin.UserId;
        await flowHub.Get<DigestFlow>(userId.Value, ct);

        var account = await accountsBackend.Get(userId, ct).Require();
        var email = ActualChat.Email.Parse($"admin{Constants.Team.EmailSuffix}");
        var updateCmd = new AccountsBackend_Update(
            account
                .WithEmailIdentity(email) with {
                TimeZone = "America/New_York",
            },
            null);
        await commander.Call(updateCmd, true, ct);

        await TestWait.When(async innerCt => {
            var flow = await flowHub.TryGet<DigestFlow>(userId.Value, innerCt);
            flow.Should().NotBeNull();
            flow.LastReadiness.IsSuspended.Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldQueueDigest()
    {
        using var cts = NewTestCts();
        var ct = cts.Token;
        var spy = new SendDigestSpy();
        await using var h = await NewAppHost(options => options with  {
            ConfigureServices = (_, services) => {
                services.AddSingleton(spy);
                services.AddCommander().AddHandlers<SendDigestSpy>();
            },
        });
        await using var tester = h.NewWebClientTester(Out);

        var flowHub = h.Services.FlowHub();
        var serverKvasBackend = h.Services.GetRequiredService<IServerKvasBackend>();
        var account = await tester.SignInAsNew("Digest", ct);
        var userId = account.Id;
        await flowHub.Get<DigestFlow>(userId.Value, ct);
        await serverKvasBackend.ForUser(userId).UserEmailsSettings()
            .Update(x => x with {
                DigestTime = DateTime.Now.TimeOfDay.Add(new TimeSpan(0, 0, 10)),
            }, ct);
        await UpdateAccount(h, userId, TimeZoneInfo.Local.Id, true, ct);

        await TestWait.When(async innerCt => {
            var flow = await flowHub.TryGet<DigestFlow>(userId.Value, innerCt);
            flow.Should().NotBeNull();
            flow.RunCount.Should().BeGreaterThan(0);
        }, TimeSpan.FromSeconds(30));
        await spy.WaitFor(userId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WakeUpShouldNotResendDigest(bool isTimeZoneChange)
    {
        // arrange
        using var cts = NewTestCts();
        var ct = cts.Token;
        var spy = new SendDigestSpy();
        await using var h = await NewAppHost(options => options with  {
            ConfigureServices = (_, services) => {
                services.AddSingleton(spy);
                services.AddCommander().AddHandlers<SendDigestSpy>();
            },
        });
        await using var tester = h.NewWebClientTester(Out);

        var flowHub = h.Services.FlowHub();
        var account = await tester.SignInAsNew("Digest", ct);
        var userId = account.Id;
        await flowHub.Get<DigestFlow>(userId.Value, ct);
        await UpdateAccount(h, userId, "America/New_York", true, ct);
        await spy.WaitFor(userId);
        var lastRunAt = (await flowHub.Get<DigestFlow>(userId.Value, ct)).LastRunAt;

        // act
        if (isTimeZoneChange)
            await UpdateAccount(h, userId, "Asia/Tokyo", true, ct);
        else
            await flowHub.NewResumeEvent<DigestFlow>(userId.Value).Schedule(ct);
        await TestWait.When(async innerCt => {
            var flow = await flowHub.TryGet<DigestFlow>(userId.Value, innerCt);
            flow.Should().NotBeNull();
            flow.LastRunAt.Should().BeGreaterThan(lastRunAt);
        }, TimeSpan.FromSeconds(30));
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        // assert
        spy.UserIds.Count(x => x == userId).Should().Be(1,
            "a wake-up before the next digest time must not send another digest");
    }

    [Fact]
    public async Task ShouldSkipDigestForRecentlyActiveUser()
    {
        using var cts = NewTestCts();
        var ct = cts.Token;
        var spy = new SendDigestSpy();
        await using var h = await NewAppHost(options => options with  {
            ConfigureServices = (_, services) => {
                services.AddSingleton(spy);
                services.AddCommander().AddHandlers<SendDigestSpy>();
            },
        });
        await using var tester = h.NewWebClientTester(Out);

        var flowHub = h.Services.FlowHub();
        var commander = h.Services.Commander();
        var serverKvasBackend = h.Services.GetRequiredService<IServerKvasBackend>();
        var account = await tester.SignInAsNew("Digest", ct);
        var userId = account.Id;
        await flowHub.Get<DigestFlow>(userId.Value, ct);
        var checkIn = new UserPresencesBackend_CheckIn(userId, h.Services.Clocks().SystemClock.Now, true);
        await commander.Call(checkIn, true, ct);
        await serverKvasBackend.ForUser(userId).UserEmailsSettings()
            .Update(x => x with {
                DigestTime = DateTime.Now.TimeOfDay.Add(new TimeSpan(0, 0, 10)),
            }, ct);
        await UpdateAccount(h, userId, TimeZoneInfo.Local.Id, true, ct);

        await TestWait.When(async innerCt => {
            var flow = await flowHub.TryGet<DigestFlow>(userId.Value, innerCt);
            flow.Should().NotBeNull();
            flow.RunCount.Should().BeGreaterThan(0);
        }, TimeSpan.FromSeconds(30));
        await Task.Delay(TimeSpan.FromSeconds(2), ct);
        spy.UserIds.Should().NotContain(userId,
            "a user who was in the app within the last day has seen what the digest would summarize");
    }

    [Fact]
    public async Task ShouldResumeDigestFlowOnEmailVerification()
    {
        using var cts = NewTestCts();
        var ct = cts.Token;
        await using var h = await NewAppHost();
        await using var tester = h.NewWebClientTester(Out);

        var flowHub = h.Services.FlowHub();
        var account = await tester.SignInAsNew("Digest", ct);
        var userId = account.Id;
        await flowHub.Get<DigestFlow>(userId.Value, ct);
        await UpdateAccount(h, userId, "America/New_York", false, ct);
        await TestWait.When(async innerCt => {
            var flow = await flowHub.TryGet<DigestFlow>(userId.Value, innerCt);
            flow.Should().NotBeNull();
            flow.LastReadiness.SuspensionReason.Should().Contain("verified email");
        }, TimeSpan.FromSeconds(30));

        // act
        await UpdateAccount(h, userId, "America/New_York", true, ct);

        // assert
        await TestWait.When(async innerCt => {
            var flow = await flowHub.TryGet<DigestFlow>(userId.Value, innerCt);
            flow.Should().NotBeNull();
            flow.LastReadiness.IsSuspended.Should().BeFalse("verifying the email must wake the flow up");
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldResumeDigestFlowOnSettingsChange()
    {
        using var cts = NewTestCts();
        var ct = cts.Token;
        await using var h = await NewAppHost();
        await using var tester = h.NewWebClientTester(Out);

        var flowHub = h.Services.FlowHub();
        var serverKvasBackend = h.Services.GetRequiredService<IServerKvasBackend>();
        var account = await tester.SignInAsNew("Digest", ct);
        var userId = account.Id;
        await serverKvasBackend.ForUser(userId).UserEmailsSettings()
            .Update(x => x with { IsDigestEnabled = false }, ct);
        await flowHub.Get<DigestFlow>(userId.Value, ct);
        await UpdateAccount(h, userId, "America/New_York", true, ct);
        await TestWait.When(async innerCt => {
            var flow = await flowHub.TryGet<DigestFlow>(userId.Value, innerCt);
            flow.Should().NotBeNull();
            flow.LastReadiness.SuspensionReason.Should().Contain("disabled");
        }, TimeSpan.FromSeconds(30));

        // act
        await tester.AppServices.UserSettingsUI(tester.Session).UserEmailsSettings()
            .Update(x => x with { IsDigestEnabled = true }, ct);

        // assert
        await TestWait.When(async innerCt => {
            var flow = await flowHub.TryGet<DigestFlow>(userId.Value, innerCt);
            flow.Should().NotBeNull();
            flow.LastReadiness.IsSuspended.Should().BeFalse("turning the digest on must wake the flow up");
        }, TimeSpan.FromSeconds(30));
    }

    // Sign-in keeps neither the time zone nor a verified email, and the team gate wants a team address
    private static async Task UpdateAccount(
        TestAppHost h, UserId userId, string timeZone, bool isEmailVerified, CancellationToken ct)
    {
        var accountsBackend = h.Services.GetRequiredService<IAccountsBackend>();
        var account = await accountsBackend.Get(userId, ct).Require();
        if (isEmailVerified) {
            var email = ActualChat.Email.Parse($"digest-{UniqueNames.Random()}{Constants.Team.EmailSuffix}");
            account = account.WithEmailIdentity(email);
        }
        var updateCmd = new AccountsBackend_Update(account with { TimeZone = timeZone }, null);
        await h.Services.Commander().Call(updateCmd, true, ct);
    }

    // Nested types

    // The real handler runs after it (a fresh user has no unread chats, so nothing is sent)
    public sealed class SendDigestSpy
    {
        public ConcurrentQueue<UserId> UserIds { get; } = new();

        // Priority 1 puts it above the final handler, which sits at 0 and ends the chain
        [CommandFilter(Priority = 1)]
        public Task OnSendDigest(EmailsBackend_SendDigest command, CommandContext context, CancellationToken ct)
        {
            UserIds.Enqueue(command.UserId);
            return context.InvokeRemainingHandlers(ct);
        }

        public Task WaitFor(UserId userId)
            => TestWait.WhenPolled(
                () => UserIds.Should().Contain(userId, "EmailsBackend_SendDigest was not queued"),
                Intervals.Fixed(TimeSpan.FromMilliseconds(200)),
                TimeSpan.FromSeconds(30));
    }
}
