using ActualChat.App.Server.Flows;
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
        await ComputedTest.When(async innerCt => {
            var flow = await flowHub.TryGet<AccountMigrationFlow>("", innerCt);
            flow.Should().NotBeNull();
        }, TimeSpan.FromSeconds(30));

        // AccountMigrationFlow should start DigestFlow(admin)
        await ComputedTest.When(async innerCt => {
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

        await ComputedTest.When(async innerCt => {
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

        await ComputedTest.When(async innerCt => {
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

        await ComputedTest.When(async innerCt => {
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
        var emailsBackend = new Mock<IEmailsBackend>(MockBehavior.Loose);

        await using var h = await NewAppHost(options => options with  {
            ConfigureServices = (_, services) => {
                services.AddSingleton(emailsBackend.Object);
            },
        });

        var flowHub = h.Services.FlowHub();
        var commander = h.Services.Commander();
        var accountsBackend = h.Services.GetRequiredService<IAccountsBackend>();
        var serverKvasBackend = h.Services.GetRequiredService<IServerKvasBackend>();

        var userId = Constants.User.Admin.UserId;
        await flowHub.Get<DigestFlow>(userId.Value, ct);

        var kvas = serverKvasBackend.ForUser(userId);
        await kvas.UserEmailsSettings()
            .Update(x => x with {
                DigestTime = DateTime.Now.TimeOfDay.Add(new TimeSpan(0, 0, 10)),
            }, ct);
        var account = await accountsBackend.Get(userId, ct).Require();
        var email = ActualChat.Email.Parse($"admin{Constants.Team.EmailSuffix}");
        var updateCmd = new AccountsBackend_Update(
            account
                .WithEmailIdentity(email) with {
                TimeZone = TimeZoneInfo.Local.Id,
            },
            null);
        await commander.Call(updateCmd, true, ct);

        await ComputedTest.When(async innerCt => {
            var flow = await flowHub.TryGet<DigestFlow>(userId.Value, innerCt);
            flow.Should().NotBeNull();
            flow.RunCount.Should().BeGreaterThan(0);
        }, TimeSpan.FromSeconds(30));
    }
}
