using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualChat.Testing.Host;
using ActualChat.Users.Flows;

namespace ActualChat.Users.IntegrationTests.Flows;

public class DigestFlowTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(DigestFlowTest)}", TestAppHostOptions.Default, @out)
{
    [Fact]
    public async Task ShouldStopFlowIfUserHasNoTimeZone()
    {
        using var h = await NewAppHost();

        var flows = h.Services.GetRequiredService<IFlows>();
        var f0 = await flows.GetOrStart<DigestFlow>("actual-admin");

        await ComputedTest.When(async ct => {
            var flow = await flows.Get<DigestFlow>(f0.Id.Arguments, ct);
            flow.Should().NotBeNull();
            flow!.Step.Should().Be(FlowSteps.OnEnded);
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldRunDigestFlowOnTimeZoneUpdate()
    {
        using var h = await NewAppHost();

        var commander = h.Services.Commander();
        var flows = h.Services.GetRequiredService<IFlows>();
        var accountsBackend = h.Services.GetRequiredService<IAccountsBackend>();

        var userId = UserId.Parse("actual-admin");
        var account = await accountsBackend.Get(userId, default);
        var updateCmd = new AccountsBackend_Update(account! with { TimeZone = "America/New_York" }, null);
        await commander.Call(updateCmd, true);

        await ComputedTest.When(async ct => {
            var flow = await flows.Get<DigestFlow>(userId, ct);
            flow.Should().NotBeNull();
            flow!.Step.Should().Be("OnCheck");
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldWaitTillDigestTime()
    {
        using var h = await NewAppHost();

        var commander = h.Services.Commander();
        var flows = h.Services.GetRequiredService<IFlows>();
        var accountsBackend = h.Services.GetRequiredService<IAccountsBackend>();

        var userId = UserId.Parse("actual-admin");
        var account = await accountsBackend.Get(userId, default).Require();
        var updateCmd = new AccountsBackend_Update(account with { TimeZone = "America/New_York" }, null);
        await commander.Call(updateCmd, true);

        await ComputedTest.When(async ct => {
            var flow = await flows.Get<DigestFlow>(userId, ct);
            flow.Should().NotBeNull();
            flow!.Step.Should().Be("OnCheck");
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ShouldQueueDigest()
    {
        var emailsBackend = new Mock<IEmailsBackend>();

        using var h = await NewAppHost(options => options with  {
            ConfigureServices = (_, services) => {
                services.AddSingleton(emailsBackend.Object);
            },
        });

        var commander = h.Services.Commander();
        var flows = h.Services.GetRequiredService<IFlows>();
        var accountsBackend = h.Services.GetRequiredService<IAccountsBackend>();
        var serverKvasBackend = h.Services.GetRequiredService<IServerKvasBackend>();

        var userId = UserId.Parse("actual-admin");
        var kvas = serverKvasBackend.GetUserClient(userId);
        var userEmailsSettings = await kvas.GetUserEmailsSettings(default);
        await kvas.SetUserEmailsSettings(
            userEmailsSettings with {
                DigestTime = DateTime.Now.TimeOfDay.Add(new TimeSpan(0, 0, 10)),
            },
            default);
        var account = await accountsBackend.Get(userId, default).Require();
        var updateCmd = new AccountsBackend_Update(account with {
            TimeZone = TimeZoneInfo.Local.Id,
        }, null);
        await commander.Call(updateCmd, true);

        await ComputedTest.When(async ct => {
            var flow = await flows.Get<DigestFlow>(userId, ct);
            flow.Should().NotBeNull();
            flow!.SendCount.Should().BeGreaterThan(0);
        }, TimeSpan.FromSeconds(30));
    }
}
