using ActualChat.Chat.Flows;
using ActualChat.Chat.ML;
using ActualChat.Flows;
using ActualChat.Testing.Flows;

namespace ActualChat.Chat.UnitTests.Flows;

public class ChatEntryEndsAtFixupFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<ChatEntryEndsAtFixupFlow>(@out)
{
    protected override ChatEntryEndsAtFixupFlow CreatePopulated() => new() {
        LastProcessedEntryId = "entry-7",
        FixedCount = 100,
        SkippedCount = 5,
        TotalScanned = 200,
    };
}

public class ChatEntryMigrationFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<ChatEntryMigrationFlow>(@out)
{
    protected override ChatEntryMigrationFlow CreatePopulated() => new() {
        LastProcessedChatId = "chat-7",
        LastProcessedLocalId = 42,
        MigratedEntryCount = 100,
        MigratedChatCount = 5,
        TotalEntryCount = 200,
        TotalChatCount = 10,
    };
}

public class ChatEntryMigrationFixupFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<ChatEntryMigrationFixupFlow>(@out)
{
    protected override ChatEntryMigrationFixupFlow CreatePopulated() => new() {
        LastProcessedChatId = "chat-7",
        LastProcessedLocalId = 42,
        MigratedEntryCount = 100,
        MigratedChatCount = 5,
        TotalEntryCount = 200,
        TotalChatCount = 10,
    };
}

public class ConversationSplitFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<ConversationSplitFlow>(@out)
{
    protected override ConversationSplitFlow CreatePopulated() => new() {
        ExtractorState = new ExtractorState(null, null),
        LastLid = 100,
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastSummaryAt = new Moment(new DateTime(2026, 4, 16, 13, 0, 0, DateTimeKind.Utc)),
        LastSummaryLidRanges = [new Range<long>(1, 10), new Range<long>(20, 30)],
        LastReadiness = "test-suspension",
    };
}

public class ConversationSplitMasterFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<ConversationSplitMasterFlow>(@out)
{
    protected override ConversationSplitMasterFlow CreatePopulated() => new() {
        // Own
        MaxVersion = 9999,
        // From IndexingFlow<IndexingFlowCursor<ChatId>>
        Cursor = new IndexingFlowCursor<ChatId>(ChatId.Parse("the-actual-one"), 42),
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastReadiness = "test-suspension",
    };
}

public class TranslationCleanupFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<TranslationCleanupFlow>(@out)
{
    protected override TranslationCleanupFlow CreatePopulated() => new() {
        // From PeriodicFlow
        RunCount = 7,
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastReadiness = "test-suspension",
    };
}

public class ChatContentIndexingMasterFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<ChatContentIndexingMasterFlow>(@out)
{
    protected override ChatContentIndexingMasterFlow CreatePopulated() => new() {
        // Own
        MaxVersion = 9999,
        // From IndexingFlow<IndexingFlowCursor<ChatId>>
        Cursor = new IndexingFlowCursor<ChatId>(ChatId.Parse("the-actual-one"), 42),
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastReadiness = "test-suspension",
    };
}

public class ChatEntryContentIndexingFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<ChatEntryContentIndexingFlow>(@out)
{
    protected override ChatEntryContentIndexingFlow CreatePopulated() => new() {
        // From IndexingFlow<IndexingFlowCursor<ChatEntryId>>
        Cursor = new IndexingFlowCursor<ChatEntryId>(ChatEntryId.New(ChatId.Parse("the-actual-one"), 42), 7),
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastReadiness = "test-suspension",
    };
}

public class ChatMediaIndexingFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<ChatMediaIndexingFlow>(@out)
{
    protected override ChatMediaIndexingFlow CreatePopulated() => new() {
        // Own
        PendingEntryLids = [1L, 2L, 3L],
        // From IndexingFlow<IndexingFlowCursor<ChatEntryId>>
        Cursor = new IndexingFlowCursor<ChatEntryId>(ChatEntryId.New(ChatId.Parse("the-actual-one"), 42), 7),
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastReadiness = "test-suspension",
    };
}
