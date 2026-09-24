using ActualChat.Sharding;

namespace ActualChat.Testing.Host;

public static class ShardOwnerTestExt
{
    public static Task WhenOwned(
        this ShardOwner shardOwner,
        TimeSpan? timeout = null,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
        // A shard runs nothing until this node has held its lock through ShardOwner.LockToUseDelay
        => TestWait.When(async ct => {
            foreach (var state in shardOwner.States) {
                var shardState = await state.Use(ct).ConfigureAwait(false);
                (shardState.HasLiveOwnership || !shardState.MustOwn).Should().BeTrue(
                    $"shard #{shardState.ShardIndex} of {shardOwner.ShardScheme.Name} must be owned or mapped elsewhere");
            }
        }, timeout, callerFilePath: callerFilePath, callerLine: callerLine);
}
