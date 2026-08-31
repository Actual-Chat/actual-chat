using ActualChat.App.Server.Flows;
using ActualChat.Flows;
using ActualChat.Testing.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

// Test flow types

public class TimerFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<TimerFlow>(@out)
{
    protected override TimerFlow CreatePopulated() => new() {
        RemainingCount = 42,
        Period = TimeSpan.FromSeconds(7),
    };
}

public class ResumeLatencyFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<ResumeLatencyFlow>(@out)
{
    protected override ResumeLatencyFlow CreatePopulated() => new() {
        RemainingCount = 5,
        LastResumeAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        Delays = [TimeSpan.FromMilliseconds(123), TimeSpan.FromSeconds(1)],
    };
}

public class SimpleIndexingFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<SimpleIndexingFlow>(@out)
{
    protected override SimpleIndexingFlow CreatePopulated() => new() {
        // From IndexingFlow<long>
        Cursor = 100L,
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastReadiness = "test-reason",
    };
}

public class SimpleBatchedIndexingFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<SimpleBatchedIndexingFlow>(@out)
{
    protected override SimpleBatchedIndexingFlow CreatePopulated() => new() {
        // From BatchedIndexingFlow → IndexingFlow<IndexingFlowCursor<ChatId>>
        Cursor = new IndexingFlowCursor<ChatId>(ChatId.Parse("the-actual-one"), 42),
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastReadiness = "test-reason",
    };
}

public class SimpleThrottledUpdateFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<SimpleThrottledUpdateFlow>(@out)
{
    protected override SimpleThrottledUpdateFlow CreatePopulated() => new() {
        // From ThrottledFlow
        SuccessCount = 10,
        FailCount = 2,
        NextRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
    };
}

public class FailingThrottledUpdateFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<FailingThrottledUpdateFlow>(@out)
{
    protected override FailingThrottledUpdateFlow CreatePopulated() => new() {
        // From ThrottledFlow
        SuccessCount = 7,
        FailCount = 3,
        NextRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
    };
}

// App.Server flow types

public class MigrationFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<MigrationFlow>(@out)
{
    protected override MigrationFlow CreatePopulated() => new();
}

public class IconSvgToPngMigrationFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<IconSvgToPngMigrationFlow>(@out)
{
    protected override IconSvgToPngMigrationFlow CreatePopulated() => new() {
        Phase = IconSvgToPngMigrationFlow.MigrationPhase.Chats,
        LastProcessedEntityId = "entity-42",
        ConvertedCount = 100,
        SkippedCount = 5,
        FailedCount = 2,
    };
}
