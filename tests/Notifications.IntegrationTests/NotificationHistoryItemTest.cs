namespace ActualChat.Notifications.IntegrationTests;

public class NotificationHistoryItemTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData(NotificationKind.Mention, true)]
    [InlineData(NotificationKind.Reply, true)]
    [InlineData(NotificationKind.Reaction, true)]
    [InlineData(NotificationKind.Attention, true)]
    [InlineData(NotificationKind.Thread, true)]
    [InlineData(NotificationKind.Invitation, true)]
    [InlineData(NotificationKind.Conversation, true)]
    [InlineData(NotificationKind.IncomingCall, true)]
    [InlineData(NotificationKind.Message, false)]
    [InlineData(NotificationKind.SpeechStarted, false)]
    [InlineData(NotificationKind.None, false)]
    [InlineData(NotificationKind.Invalid, false)]
    public void IsLoggedKindShouldMatchTheSpec(NotificationKind kind, bool isLogged)
        => NotificationHistoryItem.IsLoggedKind(kind).Should().Be(isLogged,
            "plain chat traffic is never logged, every addressed kind is");
}
