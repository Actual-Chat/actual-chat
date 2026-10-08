using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Flows;
using ActualChat.Testing.Flows;

namespace ActualChat.Chat.UnitTests;

public sealed class ChatCleanupContractTest
{
    [Fact]
    public void CleanupCommandsShouldRoundTrip()
    {
        // arrange
        var chatId = GroupChatId.New();
        var authorId = AuthorId.New(chatId, 7);
        var command = new Chats_WipeHistory {
            Session = Session.New(), ChatId = chatId, MinEntryLid = 123,
        };

        // act, assert
        command.AssertPassesThroughSerializers();
        new ChatsBackend_RequestHistoryWipe(chatId, (123, 456)).AssertPassesThroughSerializers();
        new ChatsBackend_PurgeEntryBatch(chatId).AssertPassesThroughSerializers();
        new ChatsBackend_PurgeEntryBatch(chatId, 123, (200, 300)).AssertPassesThroughSerializers();
        new ChatsBackend_RequestRemoval(chatId).AssertPassesThroughSerializers();
        new ChatEntriesPurgedEvent(chatId, [1, 2]).AssertPassesThroughSerializers(
            (actual, expected) => actual.Should().BeEquivalentTo(expected));
        new ChatsBackend_PurgeEntries(chatId, [1, 2, 3], null, 7, (10, 20)).AssertPassesThroughSerializers(
            (actual, expected) => actual.Should().BeEquivalentTo(expected));
        new ChatsBackend_PurgeAuthorEntries(chatId, authorId).AssertPassesThroughSerializers();
    }

    [Fact]
    public void CleanupFlowMessagesShouldRestoreTheirPayloads()
    {
        // arrange
        var chatId = GroupChatId.New();
        var wipeHistory = new ChatPurgeFlow.WipeHistory((100, 124));
        var removeAuthorEntries = new ChatPurgeFlow.RemoveAuthorEntries(AuthorId.New(chatId, 7));

        // act, assert
        FlowInboxMessage.New(wipeHistory).AssertPassesThroughSerializers(
            m => m.Payload.Should().Be(wipeHistory));
        FlowInboxMessage.New(removeAuthorEntries).AssertPassesThroughSerializers(
            m => m.Payload.Should().Be(removeAuthorEntries));
    }

    [Fact]
    public void RetentionShouldRoundTripThroughStorage()
    {
        // arrange
        var chat = new Chat(GroupChatId.New(), 123) {
            RetentionPeriod = TimeSpan.FromDays(1),
        };

        // act
        var restored = new DbChat(chat).ToModel();

        // assert
        restored.RetentionPeriod.Should().Be(TimeSpan.FromDays(1));
        new ChatDiff { RetentionPeriod = (TimeSpan?)null }.AssertPassesThroughSerializers();
        new ChatDiff { RetentionPeriod = TimeSpan.FromDays(1) }.AssertPassesThroughSerializers();
    }
}

public sealed class ChatPurgeFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<ChatPurgeFlow>(@out)
{
    protected override ChatPurgeFlow CreatePopulated()
        => new() {
            ClearUntilEntryLid = 42,
            RemovedAuthorIds = [AuthorId.New(GroupChatId.New(), 7)],
            WipeEntryLidRanges = [new Range<long>(50, 60), new Range<long>(70, 80)],
        };
}
