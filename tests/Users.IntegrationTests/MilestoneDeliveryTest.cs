using ActualChat.Testing.Host;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Users.IntegrationTests;

public sealed class MilestoneDeliveryTest(ITestOutputHelper @out)
    : AppHostTestBase("milestone-delivery", TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => services.Replace(
            ServiceDescriptor.Singleton<IMilestoneSender>(new FakeMilestoneSender())),
    }, @out)
{
    private IUserMilestonesBackend Backend => AppHost.Services.GetRequiredService<IUserMilestonesBackend>();
    private FakeMilestoneSender Sender => (FakeMilestoneSender)AppHost.Services.GetRequiredService<IMilestoneSender>();
    private ICommander Commander => AppHost.Services.Commander();
    private MomentClockSet Clocks => AppHost.Services.Clocks();
    private TestAppHost AppHost { get; set; } = null!;

    protected override async Task DisposeAsync()
    {
        await AppHost.DisposeAsync();
        await base.DisposeAsync();
    }

    [Fact(Timeout = 60_000)]
    public async Task MilestoneShouldWaitForConsentThenBeSent()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var now = Clocks.SystemClock.Now;
        var addCmd = new UserAttributionsBackend_Add(
            account.Id, AttributionOrigin.PlayReferrer, "utm_campaign=walkie&gclid=g1", now);
        await Commander.Call(addCmd);

        // act
        var reachCmd = new UserMilestonesBackend_Reach(account.Id, MilestoneKind.Message, now);
        await Commander.Call(reachCmd);

        // assert
        await WaitForState(account.Id, MilestoneDeliveryState.AwaitingConsent);
        Sender.SentUserIds.Should().NotContain(account.Id, "nothing goes out without consent");

        // act
        var setAppSettingsCmd = new UserSettings_Set {
            Session = tester.Session,
            Key = nameof(UserAppSettings),
            Value = new UserAppSettings { IsDataCollectionEnabled = true },
        };
        await tester.Commander.Call(setAppSettingsCmd);

        // assert
        var delivery = await WaitForState(account.Id, MilestoneDeliveryState.Sent);
        delivery.AttributionId.Should().NotBeNull("it is credited to the latest attribution");
        Sender.SentUserIds.Should().Contain(account.Id);
    }

    [Fact(Timeout = 60_000)]
    public async Task MilestoneWithoutAttributionShouldBeSkipped()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        await GiveConsent(tester);

        // act
        var reachCmd = new UserMilestonesBackend_Reach(account.Id, MilestoneKind.PeerChat, Clocks.SystemClock.Now);
        await Commander.Call(reachCmd);

        // assert
        var delivery = await WaitForState(account.Id, MilestoneDeliveryState.Skipped);
        delivery.Note.Should().NotBeEmpty("a skip says why");
        Sender.SentUserIds.Should().NotContain(account.Id);
    }

    [Fact(Timeout = 60_000)]
    public async Task TransientFailureShouldBeRetriedLater()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        await GiveConsent(tester);
        var now = Clocks.SystemClock.Now;
        var addCmd = new UserAttributionsBackend_Add(account.Id, AttributionOrigin.Url, "/?c=retry", now);
        await Commander.Call(addCmd);
        Sender.FailingUserIds.Add(account.Id);

        // act
        var reachCmd = new UserMilestonesBackend_Reach(account.Id, MilestoneKind.Conversation, now);
        await Commander.Call(reachCmd);

        // assert
        var delivery = await WaitForState(account.Id, MilestoneDeliveryState.Pending);
        delivery.Attempts.Should().Be(1);
        delivery.NextAttemptAt.Should().NotBeNull();
        delivery.NextAttemptAt!.Value.Should().BeGreaterThan(now);
    }

    // Protected/internal methods

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        AppHost = await NewAppHost();
    }

    // Private methods

    private static Task GiveConsent(IWebTester tester)
    {
        var setAppSettingsCmd = new UserSettings_Set {
            Session = tester.Session,
            Key = nameof(UserAppSettings),
            Value = new UserAppSettings { IsDataCollectionEnabled = true },
        };
        return tester.Commander.Call(setAppSettingsCmd);
    }

    private Task<MilestoneDelivery> WaitForState(UserId userId, MilestoneDeliveryState state)
        => TestWait.When(async ct => {
            var deliveries = await Backend.ListDeliveries(userId, ct);
            return deliveries.Should().ContainSingle(d => d.State == state).Subject;
        });

    // Nested types

    private sealed class FakeMilestoneSender : IMilestoneSender
    {
        public ConcurrentBag<UserId> SentUserIds { get; } = [];
        public ConcurrentBag<UserId> FailingUserIds { get; } = [];

        public MilestoneTarget Target => MilestoneTarget.GA4;
        public bool IsEnabled => true;

        public MilestoneCheck Check(MilestoneSendContext context)
            => context.Attribution is null ? MilestoneCheck.Skip("No attribution") : MilestoneCheck.Send;

        public Task<MilestoneSendOutcome> Send(MilestoneSendContext context, CancellationToken cancellationToken)
        {
            var userId = context.Milestone.UserId;
            if (FailingUserIds.Contains(userId))
                return Task.FromResult(new MilestoneSendOutcome(MilestoneSendResult.Retry, "HTTP 503"));

            SentUserIds.Add(userId);
            return Task.FromResult(new MilestoneSendOutcome(MilestoneSendResult.Sent));
        }
    }
}
