using ActualChat.MLSearch.Indexing.Initializer;
using ActualLab.Resilience;

namespace ActualChat.MLSearch.UnitTests.Indexing.Initializer;

public partial class ChatIndexInitializerShardTests
{
    [Fact]
    public async Task UpdateCursorMethodDoesNothingIfNoJobCompletedSincePreviousRun()
    {
    }

    [Fact]
    public async Task UpdateCursorMethodAdvancesCursorUpToThePointWhereAllJobsCompleted()
    {
    }

    [Fact]
    public async Task UpdateCursorMethodEvictsStallJobsAfterTimeout()
    {
    }

    [Fact]
    public async Task UpdateCursorMethodReleasesSemaphoreSlotUponJobEviction()
    {
    }

    [Fact]
    public async Task UpdateCursorMethodRetriesOnTransientError()
    {
    }

    [Fact]
    public async Task UpdateCursorMethodUsesRetrySettingSpecified()
    {
    }

    [Fact]
    public async Task UpdateCursorMethodLogsErrorBeforeRethrow()
    {
    }
}
