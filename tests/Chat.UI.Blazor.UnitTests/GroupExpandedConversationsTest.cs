using ActualChat.Chat;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public class GroupExpandedConversationsTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");

    [Fact]
    public void InterruptedConversationStaysOneBlock()
    {
        // arrange: an item that belongs to no conversation and sits past the conversation's
        // EndEntryLid - a new entry the conversation hasn't absorbed yet - lands between two of
        // its items. Splitting there emits two blocks keyed by the same StartEntryLid.
        var conversation = NewConversation(startEntryLid: 100, endEntryLid: 105);
        var messages = new ChatMessage[] {
            NewMessage(100, conversation),
            NewMessage(101, conversation),
            NewMessage(200),
            NewMessage(102, conversation),
        };

        // act
        var result = ChatUI.GroupExpandedConversations(messages, null, default, false, null, null);

        // assert
        var blocks = result.OfType<ExpandedConversationMessage>().ToList();
        blocks.Count.Should().Be(1);
        blocks[0].Items.Select(x => x.Id).Should().Equal(100, 101, 200, 102);
        result.Count.Should().Be(1);
    }

    [Fact]
    public void InterruptedConversationProducesNoDuplicateKeys()
    {
        // arrange
        var conversation = NewConversation(startEntryLid: 100, endEntryLid: 105);
        var messages = new ChatMessage[] {
            NewMessage(100, conversation),
            NewMessage(200),
            NewMessage(101, conversation),
            NewMessage(300),
            NewMessage(102, conversation),
        };

        // act
        var result = ChatUI.GroupExpandedConversations(messages, null, default, false, null, null);

        // assert: a duplicate @key among siblings is what tears the Blazor render down
        var keys = result.Select(x => ((IVirtualListItem)x).Key).ToList();
        keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TrailingItemsOutsideTheRangeDoNotOpenASecondBlock()
    {
        // arrange: nothing of the conversation follows, so these close the block rather than
        // being held inside it
        var conversation = NewConversation(startEntryLid: 100, endEntryLid: 105);
        var messages = new ChatMessage[] {
            NewMessage(100, conversation),
            NewMessage(101, conversation),
            NewMessage(500),
        };

        // act
        var result = ChatUI.GroupExpandedConversations(messages, null, default, false, null, null);

        // assert
        result.OfType<ExpandedConversationMessage>().Should().HaveCount(1);
        result[^1].Id.Should().Be(500);
    }

    [Fact]
    public void SeparateConversationsStaySeparateBlocks()
    {
        // arrange
        var first = NewConversation(startEntryLid: 100, endEntryLid: 105);
        var second = NewConversation(startEntryLid: 200, endEntryLid: 205);
        var messages = new ChatMessage[] {
            NewMessage(100, first),
            NewMessage(101, first),
            NewMessage(200, second),
            NewMessage(201, second),
        };

        // act
        var result = ChatUI.GroupExpandedConversations(messages, null, default, false, null, null);

        // assert
        var blocks = result.OfType<ExpandedConversationMessage>().ToList();
        blocks.Count.Should().Be(2);
        blocks[0].Items.Select(x => x.Id).Should().Equal(100, 101);
        blocks[1].Items.Select(x => x.Id).Should().Equal(200, 201);
    }

    // Private methods

    private static Conversation NewConversation(long startEntryLid, long endEntryLid)
        => new(ConversationId.New(TestChatId, startEntryLid)) { EndEntryLid = endEntryLid };

    private static ChatMessage NewMessage(long id, Conversation? conversation = null)
        => new TestMessage(id) { Conversation = conversation };

    // Nested types

    private sealed class TestMessage(long id) : ChatMessage(id)
    {
        public override bool Equals(ChatMessage? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => Id.GetHashCode();
    }
}
