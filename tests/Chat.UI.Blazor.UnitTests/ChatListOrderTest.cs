using ActualChat.Contacts;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ChatListOrderTest
{
    private static readonly UserId OwnerId = UserId.New();

    [Fact]
    public void ReactionsFirstShouldOrderReactedChatsByReactionTime()
    {
        // arrange
        var oldChat = NewChatInfo(version: 1);
        var busyChat = NewChatInfo(version: 100);
        var reactedAt = new Dictionary<ChatId, Moment> {
            [oldChat.Id] = Moment.EpochStart + TimeSpan.FromMinutes(5),
        };

        // act
        var ordered = new[] { busyChat, oldChat }
            .OrderBy(ChatListOrder.ByLastEventTime, ChatListPreOrder.ReactionsFirst, reactedAt)
            .ToList();

        // assert
        ordered[0].Id.Should().Be(oldChat.Id, because: "a reaction must surface the chat even on an old message");
        ordered[1].Id.Should().Be(busyChat.Id);
    }

    [Fact]
    public void ReactionsFirstWithNoReactionsShouldMatchLastEventTime()
    {
        // arrange
        var older = NewChatInfo(version: 1);
        var newer = NewChatInfo(version: 100);

        // act
        var ordered = new[] { older, newer }
            .OrderBy(ChatListOrder.ByLastEventTime, ChatListPreOrder.ReactionsFirst, null)
            .ToList();

        // assert
        ordered.Select(x => x.Id).Should().Equal([newer.Id, older.Id]);
    }

    [Fact]
    public void ChatListWithLiftedChatsShouldPutThemAbovePinned()
    {
        // arrange
        var pinnedChat = NewChatInfo(version: 100, isPinned: true);
        var pingedChat = NewChatInfo(version: 1);
        var liftedAt = new Dictionary<ChatId, Moment> {
            [pingedChat.Id] = Moment.EpochStart + TimeSpan.FromMinutes(5),
        };

        // act
        var ordered = new[] { pinnedChat, pingedChat }
            .OrderBy(ChatListOrder.ByLastEventTime, ChatListPreOrder.ChatList, liftedAt)
            .ToList();

        // assert
        ordered.Select(x => x.Id).Should().Equal([pingedChat.Id, pinnedChat.Id],
            because: "an attention ping outranks pinning on the Mentions tab");
    }

    [Fact]
    public void ChatListWithoutLiftedChatsShouldKeepPinnedFirst()
    {
        // arrange
        var pinnedChat = NewChatInfo(version: 1, isPinned: true);
        var busyChat = NewChatInfo(version: 100);

        // act
        var ordered = new[] { busyChat, pinnedChat }
            .OrderBy(ChatListOrder.ByLastEventTime, ChatListPreOrder.ChatList)
            .ToList();

        // assert
        ordered.Select(x => x.Id).Should().Equal([pinnedChat.Id, busyChat.Id]);
    }

    // Private methods

    private static ChatInfo NewChatInfo(long version, bool isPinned = false)
    {
        var chatId = ChatId.Parse(GroupChatId.New().Value);
        var contactId = ContactId.NewAny(OwnerId, chatId);
        return new ChatInfo(new Contact(contactId, version) {
            Chat = new Chat(chatId),
            IsPinned = isPinned,
        });
    }
}
