using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public sealed class UserMilestonesTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IUserMilestonesBackend Backend => AppHost.Services.GetRequiredService<IUserMilestonesBackend>();

    [Fact(Timeout = 90_000)]
    public async Task ReplyInGroupChatShouldGiveBothUsersConversation()
    {
        // arrange
        await using var aliceTester = AppHost.NewWebClientTester(Out);
        await using var bobTester = AppHost.NewWebClientTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        var bob = await bobTester.SignInAsUniqueBob();
        var (chatId, inviteId) = await aliceTester.CreateChat(false);

        // act
        await aliceTester.CreateTextEntry(chatId, "Hi");

        // assert
        await WaitForKinds(alice.Id, MilestoneKind.Message);
        var aliceMilestones = await Backend.List(alice.Id, default);
        aliceMilestones.Should().NotContain(m => m.Kind == MilestoneKind.Conversation, "nobody else has posted yet");

        // act
        await bobTester.JoinChat(chatId, inviteId);
        await bobTester.CreateTextEntry(chatId, "Hello");

        // assert
        await WaitForKinds(bob.Id, MilestoneKind.Message, MilestoneKind.Conversation);
        await WaitForKinds(alice.Id, MilestoneKind.Message, MilestoneKind.Conversation);
        var bobMilestones = await Backend.List(bob.Id, default);
        bobMilestones.Should().NotContain(m => m.Kind == MilestoneKind.PeerChat, "it is a group chat");
    }

    [Fact(Timeout = 90_000)]
    public async Task PostInPeerChatShouldReachPeerChat()
    {
        // arrange
        await using var aliceTester = AppHost.NewWebClientTester(Out);
        await using var bobTester = AppHost.NewWebClientTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        var alice = await aliceTester.SignInAsUniqueAlice();
        await aliceTester.CreatePeerContact(alice, bob);
        var chatId = (ChatId)PeerChatId.New(bob.Id, alice.Id);

        // act
        await aliceTester.CreateTextEntry(chatId, "Hi Bob");

        // assert
        await WaitForKinds(alice.Id, MilestoneKind.Message, MilestoneKind.PeerChat);
    }

    [Fact(Timeout = 60_000)]
    public async Task FinishedOnboardingShouldReachOnboardingCompleted()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();

        var recordOnboardingStepCmd = new Usage_RecordOnboardingStep {
            Session = tester.Session,
            Step = OnboardingSteps.Finished,
            IsCompleted = true,
        };

        // act
        await tester.Commander.Call(recordOnboardingStepCmd);

        // assert
        await WaitForKinds(account.Id, MilestoneKind.Onboarded);
    }

    [Fact(Timeout = 60_000)]
    public async Task PostOnSecondDayShouldReachDay2Return()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var (chatId, _) = await tester.CreateChat(false);
        // A post on the next UTC day, still within 48 h of sign-up
        var nextDay = UsageDay.DayOf(Clocks.SystemClock.Now) + TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1);
        var recordCmd = new UsageBackend_Record(account.Id, ApiArray.New(
            new UsageEvent(UsageEventKind.Message, nextDay, $"test-{account.Id}-next-day", 1)));
        await Commander.Call(recordCmd);

        // act
        await tester.CreateTextEntry(chatId, "Back again");

        // assert
        await WaitForKinds(account.Id, MilestoneKind.Day2Return);
    }

    [Fact(Timeout = 60_000)]
    public async Task MilestoneShouldBeReachedOnce()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var now = Clocks.SystemClock.Now;
        var reachCmd = new UserMilestonesBackend_Reach(account.Id, MilestoneKind.Conversation, now);
        var reachAgainCmd = reachCmd with { ReachedAt = now + TimeSpan.FromMinutes(1) };

        // act
        var first = await Commander.Call(reachCmd);
        var second = await Commander.Call(reachAgainCmd);
        var list = await Backend.List(account.Id, default);

        // assert
        first.Should().NotBeNull();
        second.Should().BeNull();
        list.Should().ContainSingle().Which.Id.Should().Be(first!.Id, "the first reach is kept");
    }

    // Private methods

    private Task WaitForKinds(UserId userId, params MilestoneKind[] kinds)
        => TestWait.When(async ct => {
            var milestones = await Backend.List(userId, ct);
            milestones.Select(m => m.Kind).Should().Contain(kinds);
        });
}
