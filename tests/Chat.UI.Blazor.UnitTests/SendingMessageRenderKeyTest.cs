using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public sealed class SendingMessageRenderKeyTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private static readonly AuthorId TestAuthorId = AuthorId.New(TestChatId, 1);

    [Fact]
    public void SendKeepsRenderKeysWhenItsStoredEntryReplacesIt()
    {
        // arrange: the copy guessed lid 11, the stored entry got 12 - someone else posted in between
        var before = NewMessage(10, "", ChatMessageFlags.BlockStart);
        var send = NewMessage(11, "client-1", kind: ChatMessageKind.SendingNewMessage, version: 0);
        var stored = NewMessage(12, "client-1");

        // act
        var sendGroup = ChatUI.GroupAuthorMessages([before, send]).Single();
        var storedGroup = ChatUI.GroupAuthorMessages([before, stored]).Single();

        // assert
        sendGroup.Should().BeOfType<ChatEntryAuthorGroup>()
            .Which.Items.Should().HaveCount(2);
        ((IVirtualListItem)send).RenderKey.Should().Be("c-client-1");
        ((IVirtualListItem)stored).RenderKey.Should().Be(((IVirtualListItem)send).RenderKey);
        ((IVirtualListItem)storedGroup).RenderKey.Should().Be(((IVirtualListItem)sendGroup).RenderKey);
    }

    [Fact]
    public void SendOpeningAGroupKeysItByItsClientId()
    {
        // arrange
        var send = NewMessage(11, "client-1", ChatMessageFlags.BlockStart, ChatMessageKind.SendingNewMessage, 0);
        var stored = NewMessage(12, "client-1", ChatMessageFlags.BlockStart);

        // act
        var sendGroup = ChatUI.GroupAuthorMessages([send]).Single();
        var storedGroup = ChatUI.GroupAuthorMessages([stored]).Single();

        // assert
        ((IVirtualListItem)sendGroup).RenderKey.Should().Be("c-client-1-group");
        ((IVirtualListItem)storedGroup).RenderKey.Should().Be(((IVirtualListItem)sendGroup).RenderKey);
    }

    [Fact]
    public void SeparatorsBuiltFromAnEntryKeepTheirOwnKeys()
    {
        // arrange
        var entry = NewEntry(12, "client-1", 1);
        var dateLine = new ChatEntryMessage(entry) { Kind = ChatMessageKind.DateLine };
        var message = new ChatEntryMessage(entry);

        // act
        var dateLineKey = ((IVirtualListItem)dateLine).RenderKey;

        // assert
        dateLineKey.Should().Be(((IVirtualListItem)dateLine).Key);
        dateLineKey.Should().NotBe(((IVirtualListItem)message).RenderKey);
    }

    [Fact]
    public void SendWhoseEntryIsAlreadyStoredIsRemoved()
    {
        // arrange
        var stored = NewMessage(12, "client-1");
        var send = NewMessage(13, "client-1", kind: ChatMessageKind.SendingNewMessage, version: 0);
        var otherSend = NewMessage(14, "client-2", kind: ChatMessageKind.SendingNewMessage, version: 0);
        List<ChatMessage> items = [stored, send, otherSend];

        // act
        ChatUI.RemoveSendsAlreadyStored(items);

        // assert
        items.Should().Equal(stored, otherSend);
    }

    // Private methods

    private static ChatEntryMessage NewMessage(
        long lid,
        string clientId,
        ChatMessageFlags flags = default,
        ChatMessageKind kind = ChatMessageKind.None,
        long version = 1)
        => new(NewEntry(lid, clientId, version)) { Flags = flags, Kind = kind };

    private static ChatEntry NewEntry(long lid, string clientId, long version)
        => new TextEntry(ChatEntryId.New(TestChatId, lid), version) {
            AuthorId = TestAuthorId,
            ClientId = clientId,
        };
}
