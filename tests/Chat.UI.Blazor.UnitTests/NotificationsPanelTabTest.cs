using ActualChat.Notifications;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class NotificationsPanelTabTest
{
    private static readonly UserId OwnUserId = UserId.New();
    private static readonly ChatId GroupChatId = ChatId.Parse("s-P7oXNDTeHL-752w3sfrad");
    private static readonly ChatEntryId EntryId = ChatEntryId.New(GroupChatId, 1234);
    private static readonly Symbol All = ChatListFilter.Unread.Id;
    private static readonly Symbol People = ChatListFilter.UnreadPeople.Id;
    private static readonly Symbol Mentions = ChatListFilter.UnreadMentions.Id;
    private static readonly Symbol Reactions = NotificationsUI.ReactionsFilterId;

    [Fact]
    public void MentionShouldBelongToMentionsThenAll()
    {
        // arrange
        var id = MentionNotification.New(OwnUserId, EntryId).Id;

        // act
        var tabIds = NotificationsPanelUI.ListTabIds(id);

        // assert
        tabIds.Should().Equal(Mentions, All);
    }

    [Fact]
    public void ReactionShouldBelongToReactionsThenAll()
    {
        // arrange
        var id = ReactionNotification.New(OwnUserId, EntryId).Id;

        // act
        var tabIds = NotificationsPanelUI.ListTabIds(id);

        // assert
        tabIds.Should().Equal(Reactions, All);
    }

    [Fact]
    public void MessageInAPeerChatShouldBelongToPeopleAllThenMentions()
    {
        // arrange
        var peerChatId = PeerChatId.New(OwnUserId, UserId.New());
        var id = MessageNotification.New(OwnUserId, peerChatId).Id;

        // act
        var tabIds = NotificationsPanelUI.ListTabIds(id);

        // assert
        tabIds.Should().Equal(People, All, Mentions);
    }

    [Fact]
    public void MessageInAGroupShouldBelongToAllThenMentions()
    {
        // arrange
        var id = MessageNotification.New(OwnUserId, GroupChatId).Id;

        // act
        var tabIds = NotificationsPanelUI.ListTabIds(id);

        // assert
        tabIds.Should().Equal(All, Mentions);
    }

    [Fact]
    public void ChatLevelIdShouldNotMoveThePanelOffTheMentionsTab()
    {
        // arrange
        var id = NotificationsUI.GetChatNotificationId(OwnUserId, GroupChatId);

        // act
        var tabId = NotificationsPanelUI.ChooseTabId(id, Mentions, _ => true);

        // assert
        tabId.Should().Be(Mentions, "a Mentions row with nothing bound to it links to its chat's own id");
    }

    [Fact]
    public void LastTabShouldStayWhenTheNotificationIsOnIt()
    {
        // arrange
        var id = MentionNotification.New(OwnUserId, EntryId).Id;

        // act
        var tabId = NotificationsPanelUI.ChooseTabId(id, All, _ => true);

        // assert
        tabId.Should().Be(All, "the mention is listed on All too, so the panel isn't flipped to Mentions");
    }

    [Fact]
    public void ClosestTabShouldBeChosenWhenTheLastOneDoesNotHaveTheNotification()
    {
        // arrange
        var id = ReactionNotification.New(OwnUserId, EntryId).Id;

        // act
        var tabId = NotificationsPanelUI.ChooseTabId(id, Mentions, _ => true);

        // assert
        tabId.Should().Be(Reactions);
    }

    [Fact]
    public void AllShouldBeChosenWhenTheClosestTabIsNotShown()
    {
        // arrange
        var id = ReactionNotification.New(OwnUserId, EntryId).Id;

        // act
        var tabId = NotificationsPanelUI.ChooseTabId(id, Mentions, shownTabId => shownTabId == All);

        // assert
        tabId.Should().Be(All);
    }

    [Fact]
    public void AllShouldBeChosenWhenNoTabIsShown()
    {
        // arrange
        var id = MentionNotification.New(OwnUserId, EntryId).Id;

        // act
        var tabId = NotificationsPanelUI.ChooseTabId(id, People, _ => false);

        // assert
        tabId.Should().Be(All);
    }
}
