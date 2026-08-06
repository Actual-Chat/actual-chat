using ActualChat.Testing.Flows;
using ActualChat.Users.Flows;

namespace ActualChat.Users.UnitTests.Flows;

public class AccountMigrationFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<AccountMigrationFlow>(@out)
{
    protected override AccountMigrationFlow CreatePopulated() => new() {
        LastProcessedId = "user-7",
        MigratedCount = 100,
        TotalCount = 200,
    };
}

public class UserSignInFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<UserSignInFlow>(@out)
{
    protected override UserSignInFlow CreatePopulated() => new() {
        IsAvatarUpdated = true,
    };
}

public class DigestFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<DigestFlow>(@out)
{
    protected override DigestFlow CreatePopulated() => new() {
        // From PeriodicFlow
        RunCount = 7,
        LastRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
        LastReadiness = "test-suspension",
    };
}
