using ActualChat.Flows;
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
            flow?.Step.Should().Be("OnRemove");
        }, TimeSpan.FromSeconds(30));

        await ComputedTest.When(async ct => {
            var flow = await flows.Get<DigestFlow>(f0.Id.Arguments, ct);
            flow.Should().BeNull();
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
        var accountUpdate = new AccountsBackend_Update(account! with { TimeZone = "America/New_York" }, account.Version);
        await commander.Call(accountUpdate, true);

        await ComputedTest.When(async ct => {
            var flow = await flows.Get<DigestFlow>(userId, ct);
            flow.Should().NotBeNull();
            flow?.Step.Should().Be("OnTimer");
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
        var account = await accountsBackend.Get(userId, default);
        var accountUpdate = new AccountsBackend_Update(account! with { TimeZone = "America/New_York" }, account.Version);
        await commander.Call(accountUpdate, true);

        await ComputedTest.When(async ct => {
            var flow = await flows.Get<DigestFlow>(userId, ct);
            flow.Should().NotBeNull();
            flow?.Step.Should().Be("OnTimer");
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
        var account = await accountsBackend.Get(userId, default);
        var accountUpdate = new AccountsBackend_Update(
            account! with {
                TimeZone = TimeZoneInfo.Local.Id,
            },
            account.Version);
        await commander.Call(accountUpdate, true);

        await ComputedTest.When(async _ => {
            emailsBackend.Verify(
                x => x.OnSendDigest(It.IsAny<EmailsBackend_SendDigest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            await Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }
}
