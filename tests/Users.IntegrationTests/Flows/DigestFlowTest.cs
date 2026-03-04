using ActualChat.App.Server.Flows;
using ActualChat.Testing.Host;
using ActualChat.Users.Flows;

namespace ActualChat.Users.IntegrationTests.Flows;

public class DigestFlowTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(DigestFlowTest)}", TestAppHostOptions.Default, @out)
{
    [Fact]
    public async Task MigrationFlow_Should_Start_DigestFlow()
    {
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();
        await flowHub.Get<MigrationFlow>(""); // We need to manually start it in this test
        var userId = Constants.User.Admin.UserId;

        // MigrationFlow should start AccountMigrationFlow
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<AccountMigrationFlow>("", ct);
            flow.Should().NotBeNull();
        }, TimeSpan.FromSeconds(30));

        // AccountMigrationFlow should start DigestFlow(admin)
        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<DigestFlow>(userId.Value, ct);
            flow.Should().NotBeNull();
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldStopFlowIfUserHasNoTimeZone()
    {
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();

        var userId = Constants.User.Admin.UserId.Value;
        var f0 = await flowHub.Get<DigestFlow>(userId);

        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<DigestFlow>(f0.Id.Arguments, ct);
            flow.Should().NotBeNull();
            flow.LastReadiness.IsSuspended.Should().BeTrue();
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldRunDigestFlowOnTimeZoneUpdate()
    {
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();
        var commander = h.Services.Commander();
        var accountsBackend = h.Services.GetRequiredService<IAccountsBackend>();

        var userId = Constants.User.Admin.UserId;
        await flowHub.Get<DigestFlow>(userId.Value);

        var account = await accountsBackend.Get(userId, CancellationToken.None).Require();
        var email = ActualChat.Email.Parse($"admin{Constants.Team.EmailSuffix}");
        var updateCmd = new AccountsBackend_Update(
            account
                .WithEmailIdentity(email) with {
                TimeZone = "America/New_York",
            },
            null);
        await commander.Call(updateCmd, true);

        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<DigestFlow>(userId.Value, ct);
            flow.Should().NotBeNull();
            flow.LastReadiness.IsSuspended.Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldWaitTillDigestTime()
    {
        await using var h = await NewAppHost();

        var flowHub = h.Services.FlowHub();
        var commander = h.Services.Commander();
        var accountsBackend = h.Services.GetRequiredService<IAccountsBackend>();

        var userId = Constants.User.Admin.UserId;
        await flowHub.Get<DigestFlow>(userId.Value);

        var account = await accountsBackend.Get(userId, default).Require();
        var email = ActualChat.Email.Parse($"admin{Constants.Team.EmailSuffix}");
        var updateCmd = new AccountsBackend_Update(
            account
                .WithEmailIdentity(email) with {
                TimeZone = "America/New_York",
            },
            null);
        await commander.Call(updateCmd, true);

        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<DigestFlow>(userId.Value, ct);
            flow.Should().NotBeNull();
            flow.LastReadiness.IsSuspended.Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldQueueDigest()
    {
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
        await flowHub.Get<DigestFlow>(userId.Value);

        var kvas = serverKvasBackend.GetUserClient(userId);
        await kvas.UserEmailsSettings()
            .Update(x => x with {
                DigestTime = DateTime.Now.TimeOfDay.Add(new TimeSpan(0, 0, 10)),
            });
        var account = await accountsBackend.Get(userId, default).Require();
        var email = ActualChat.Email.Parse($"admin{Constants.Team.EmailSuffix}");
        var updateCmd = new AccountsBackend_Update(
            account
                .WithEmailIdentity(email) with {
                TimeZone = TimeZoneInfo.Local.Id,
            },
            null);
        await commander.Call(updateCmd, true);

        await ComputedTest.When(async ct => {
            var flow = await flowHub.TryGet<DigestFlow>(userId.Value, ct);
            flow.Should().NotBeNull();
            flow.RunCount.Should().BeGreaterThan(0);
        }, TimeSpan.FromSeconds(30));
    }
}
