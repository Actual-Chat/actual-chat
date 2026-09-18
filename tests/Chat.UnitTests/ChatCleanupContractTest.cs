using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Testing.Flows;

namespace ActualChat.Chat.UnitTests;

public sealed class ChatCleanupContractTest
{
    [Fact]
    public void CleanupCommandsShouldRoundTrip()
    {
        // arrange
        var chatId = GroupChatId.New();
        var command = new Chats_Cleanup {
            Session = Session.New(), ChatId = chatId, MinVisibleEntryLid = 123, ExpectedVersion = 456,
        };

        // act, assert
        command.AssertPassesThroughSerializers();
        new ChatsBackend_AdvanceVisibilityBoundary(chatId, 123, 456).AssertPassesThroughSerializers();
        new ChatsBackend_Cleanup(chatId).AssertPassesThroughSerializers();
        new ChatsBackend_MarkForRemoval(chatId).AssertPassesThroughSerializers();
        new ChatEntriesPurgedEvent(chatId, [1, 2]).AssertPassesThroughSerializers(
            (actual, expected) => actual.Should().BeEquivalentTo(expected));
        new ChatsBackend_PurgeEntries(chatId, [1, 2, 3]).AssertPassesThroughSerializers(
            (actual, expected) => actual.Should().BeEquivalentTo(expected));
    }

    [Fact]
    public void RetentionAndBoundaryShouldRoundTripThroughStorage()
    {
        // arrange
        var chat = new Chat(GroupChatId.New(), 123) {
            MinVisibleEntryLid = 42,
            RetentionPeriod = TimeSpan.FromDays(1),
        };

        // act
        var restored = new DbChat(chat).ToModel();

        // assert
        restored.MinVisibleEntryLid.Should().Be(42);
        restored.RetentionPeriod.Should().Be(TimeSpan.FromDays(1));
        new ChatDiff { RetentionPeriod = (TimeSpan?)null }.AssertPassesThroughSerializers();
        new ChatDiff { RetentionPeriod = TimeSpan.FromDays(1) }.AssertPassesThroughSerializers();
    }
}

public sealed class ChatCleanupFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<ChatCleanupFlow>(@out)
{
    protected override ChatCleanupFlow CreatePopulated() => new();
}
