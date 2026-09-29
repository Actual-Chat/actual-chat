using ActualChat.Chat.Coach;
using ActualChat.Users;

namespace ActualChat.Chat.UnitTests.Coach;

public class CoachScopeTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatUserSettings Inherit = new();
    private static readonly ChatUserSettings On = new() { IsCoachingEnabled = true };
    private static readonly ChatUserSettings Off = new() { IsCoachingEnabled = false };
    private static readonly UserCoachSettings User = new();

    [Theory]
    [InlineData(null, null, false, true)]
    [InlineData(false, null, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(null, false, false, false)]
    [InlineData(null, true, false, true)]
    [InlineData(null, null, true, true)]
    public void GroupChatShouldFollowChatThenPlaceThenUser(bool? chat, bool? place, bool skipPeers, bool expected)
    {
        // arrange
        var chatId = PlaceChatId.New(PlaceId.New());
        var chatSettings = new ChatUserSettings { IsCoachingEnabled = chat };
        var placeSettings = new ChatUserSettings { IsCoachingEnabled = place };

        // act
        var inScope = CoachScope.IsInScope(chatId, chatSettings, placeSettings, User with { SkipPeerChats = skipPeers });

        // assert
        inScope.Should().Be(expected);
    }

    [Fact]
    public void PeerChatShouldBeSkippedOnlyByTheSkipFlagOrItsOwnFlag()
    {
        // arrange
        var peer = PeerChatId.New(UserId.New(), UserId.New());

        // act & assert
        CoachScope.IsInScope(peer, Inherit, null, User).Should().BeTrue();
        CoachScope.IsInScope(peer, Inherit, null, User with { SkipPeerChats = true }).Should().BeFalse();
        CoachScope.IsInScope(peer, On, null, User with { SkipPeerChats = true }).Should().BeTrue("the chat flag wins");
        CoachScope.IsInScope(peer, Off, null, User).Should().BeFalse();
    }
}
