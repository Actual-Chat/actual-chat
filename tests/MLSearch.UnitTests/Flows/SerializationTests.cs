using ActualChat.Flows;
using ActualChat.MLSearch.Flows;
using ActualChat.Testing.Flows;

namespace ActualChat.MLSearch.UnitTests.Flows;

public class AccountIndexingFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<AccountIndexingFlow>(@out)
{
    protected override AccountIndexingFlow CreatePopulated() => new() {
        Cursor = new IndexingFlowCursor<UserId>(UserId.New(), 42),
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastReadiness = "test-suspension",
    };
}

public class EntryIndexingFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<EntryIndexingFlow>(@out)
{
    protected override EntryIndexingFlow CreatePopulated() => new() {
        Cursor = new IndexingFlowCursor<ChatEntryId>(
            ChatEntryId.Parse("the-actual-one:0:7"), 42),
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastReadiness = "test-suspension",
    };
}

public class EntryIndexingMasterFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<EntryIndexingMasterFlow>(@out)
{
    protected override EntryIndexingMasterFlow CreatePopulated() => new() {
        MaxVersion = 9999,
        Cursor = new IndexingFlowCursor<ChatId>(ChatId.Parse("the-actual-one"), 42),
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastReadiness = "test-suspension",
    };
}

public class GroupIndexingFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<GroupIndexingFlow>(@out)
{
    protected override GroupIndexingFlow CreatePopulated() => new() {
        Cursor = new IndexingFlowCursor<ChatId>(ChatId.Parse("the-actual-one"), 42),
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastReadiness = "test-suspension",
    };
}

public class PlaceContactIndexingFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<PlaceContactIndexingFlow>(@out)
{
    protected override PlaceContactIndexingFlow CreatePopulated() => new() {
        Cursor = new IndexingFlowCursor<ContactId>(
            ContactId.NewChat(UserId.New(), GroupChatId.Parse("the-actual-one")), 42),
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastReadiness = "test-suspension",
    };
}

public class PlaceIndexingFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<PlaceIndexingFlow>(@out)
{
    protected override PlaceIndexingFlow CreatePopulated() => new() {
        Cursor = new IndexingFlowCursor<PlaceId>(PlaceId.Parse("xxxxxxxxxxxxx"), 42),
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastReadiness = "test-suspension",
    };
}

public class UserContactIndexingFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<UserContactIndexingFlow>(@out)
{
    protected override UserContactIndexingFlow CreatePopulated() => new() {
        Cursor = new IndexingFlowCursor<ContactId>(
            ContactId.NewChat(UserId.New(), GroupChatId.Parse("the-actual-one")), 42),
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastReadiness = "test-suspension",
    };
}
