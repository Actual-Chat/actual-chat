using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

// Pre-2.22 clients call HasNotifiedMentionedMembers, and pre-2.22 servers stored a single
// per-entry alert record instead of one per mentioned author.

[Collection(nameof(NotificationCollection))]
public sealed class MentionAlertCompatibilityTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private INotifications Notifications => Tester.AppServices.GetRequiredService<INotifications>();

    [Fact]
    public async Task PreviousReleaseClientShouldSeeAlertSentWithoutAuthorIds()
    {
        // arrange
        var entry = await CreateEntryMentioningAlice();
#pragma warning disable CS0618 // Old clients still call it
        var before = await Notifications.HasNotifiedMentionedMembers(Tester.Session, entry.Id, default);
#pragma warning restore CS0618

        // act
        var notifyCmd = new Notifications_NotifyMentionedMembers { Session = Tester.Session, ChatEntryId = entry.Id };
        await Tester.Commander.Call(notifyCmd);

        // assert
        before.Should().BeFalse();
        await TestWait.When(async ct => {
#pragma warning disable CS0618 // Old clients still call it
            var isNotified = await Notifications.HasNotifiedMentionedMembers(Tester.Session, entry.Id, ct);
#pragma warning restore CS0618
            isNotified.Should().BeTrue();
        });
    }

    [Fact]
    public async Task AlertRecordedByPreviousReleaseShouldCountAsNotified()
    {
        // arrange
        var entry = await CreateEntryMentioningAlice();
        var bob = await Tester.GetOwnAccount();
        var legacyId = ExplicitNotificationId.New(
            bob.Id, ExplicitNotificationKind.NotifyMentionedMembers, entry.Id.Value);

        // act
        var upsertCmd = new NotificationsBackend_UpsertExplicitNotification(new ExplicitNotification(legacyId));
        await Commander.Call(upsertCmd);

        // assert
        await TestWait.When(async ct => {
            var alerts = await Notifications.ListMentionedMemberAlerts(Tester.Session, entry.Id, ct);
            alerts.Should().ContainSingle().Which.IsNotified.Should().BeTrue();
        });
    }

    // Private methods

    private async Task<ChatEntry> CreateEntryMentioningAlice()
    {
        var alice = await Tester.SignInAsUniqueAlice();
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false);
        await Tester.InviteToChat(chatId, alice.Id);
        return await Tester.CreateTextEntry(chatId, $"hey @u:{alice.Id} !");
    }
}
