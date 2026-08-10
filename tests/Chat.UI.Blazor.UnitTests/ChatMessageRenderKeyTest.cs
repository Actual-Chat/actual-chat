using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ChatMessageRenderKeyTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void RenderKeyShouldFollowKeyWhenNoStableLidIsSet()
    {
        // arrange
        var message = new ConversationMessage(NewConversation(100));

        // act
        var renderKey = ((IVirtualListItem)message).RenderKey;

        // assert
        renderKey.Should().Be(message.Key.Value);
    }

    [Fact]
    public void RenderKeyShouldPinToTheStableLidWhileKeyFollowsTheConversationId()
    {
        // arrange - while live the block renders under V; StableRenderLid pins it to the
        // materialized lid C, which is what it will be keyed by once the session closes
        var liveEra = new ConversationMessage(NewConversation(100)) { StableRenderLid = 60 };

        // act
        var renderKey = ((IVirtualListItem)liveEra).RenderKey;

        // assert
        renderKey.Should().Be(ChatMessageKey.Format(liveEra.Kind, 60));
        liveEra.Key.Value.Should().Be(ChatMessageKey.Format(liveEra.Kind, 100));
    }

    [Fact]
    public void RenderKeyShouldSurviveTheLiveToMaterializedIdChange()
    {
        // arrange - the same block before and after close: id moves V(100) -> C(60)
        var beforeClose = new ConversationMessage(NewConversation(100)) { StableRenderLid = 60 };
        var afterClose = new ConversationMessage(NewConversation(60));

        // act
        var keyBefore = ((IVirtualListItem)beforeClose).RenderKey;
        var keyAfter = ((IVirtualListItem)afterClose).RenderKey;

        // assert
        keyBefore.Should().Be(keyAfter);
    }

    [Fact]
    public void ExpandedBlockRenderKeyShouldSurviveTheLiveToMaterializedIdChange()
    {
        // arrange - the wrapper <li> whose key change tears down the whole subtree
        var beforeClose = new ExpandedConversationMessage(NewConversation(100), []) { StableRenderLid = 60 };
        var afterClose = new ExpandedConversationMessage(NewConversation(60), []);

        // act
        var keyBefore = ((IVirtualListItem)beforeClose).RenderKey;
        var keyAfter = ((IVirtualListItem)afterClose).RenderKey;

        // assert
        keyBefore.Should().Be(keyAfter);
        keyBefore.Should().Be(ChatMessageKey.Format(ChatMessageKind.ConversationBlock, 60));
    }

    private static Conversation NewConversation(long startEntryLid)
        => new(ConversationId.New(TestChatId, startEntryLid), 0) { EndEntryLid = startEntryLid + 10 };
}
