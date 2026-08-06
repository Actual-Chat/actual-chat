using ActualChat.Live;
using ActualChat.Testing.Host;

namespace ActualChat.Streaming.IntegrationTests;

/// <summary>
/// Verifies that a <see cref="ILiveAudioBackend.List"/> computed captured on a node
/// gets invalidated when that node loses ownership of the corresponding shard to
/// another node joining the mesh.
///
/// This is the regression test for the bug where subscribers of a Distributed compute
/// service never received invalidations after shard migration, because the compute
/// method did not depend on shard ownership state. The failure mode is most visible
/// on the empty-result early-return path, which previously had zero dependencies.
/// </summary>
public class LiveAudioBackendShardMigrationTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(LiveAudioBackendShardMigrationTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 30_000)]
    public async Task EmptyListComputedOnHost1_ShouldInvalidateWhenShardMigratesToHost2()
    {
        var shardScheme = ShardScheme.LiveBackend;
        var syncTimeout = TimeSpan.FromSeconds(30);

        // --- Phase 1: start host1 alone; it owns every LiveBackend shard ---
        await using var h1 = await NewAppHost(o => o with { MeshLockSubspace = "x-liveAudioShardMig" });
        var w1 = h1.Services.GetRequiredService<MeshWatcher>();
        var s1 = h1.Services.GetRequiredService<ILiveAudioBackend>();
        var o1 = h1.Services.ShardOwner(shardScheme);
        await w1.WhenAnnounced;
        WriteLine($"h1 announced: {w1.ThisNode.Ref}");

        // Wait until h1 owns all shards (it's alone in the mesh).
        await ComputedTest.When(async ct => {
            var bits = await o1.BitmapState.Use(ct).ConfigureAwait(false);
            bits.SetBitCount().Should().Be(shardScheme.ShardCount);
        }, syncTimeout);

        // --- Phase 2: capture an empty-list Computed for every shard on h1 ---
        // The empty-result early-return branch is the canonical buggy path: before the
        // fix the resulting computed had ZERO dependencies and could never invalidate.
        // After the fix it must depend on shard ownership.
        var chatIdPerShard = new Dictionary<int, ChatId>();
        for (var i = 0; i < 10_000 && chatIdPerShard.Count < shardScheme.ShardCount; i++) {
            var chatId = ChatId.Parse($"tchatmig{i:D6}"); // length 14, alphanumeric
            var shardIndex = shardScheme.GetShardIndex(chatId);
            chatIdPerShard.TryAdd(shardIndex, chatId);
        }
        chatIdPerShard.Count.Should().Be(shardScheme.ShardCount,
            "must be able to pick one chat per shard from the test id space");

        var capturedPerShard = new Dictionary<int, Computed<ApiArray<LiveAudioStreamInfo>>>();
        foreach (var (shardIndex, chatId) in chatIdPerShard) {
            var computed = await Computed.Capture(() => s1.List(chatId, CancellationToken.None));
            computed.Value.Should().BeEmpty($"no streams registered yet for chat {chatId}");
            computed.IsConsistent().Should().BeTrue();
            capturedPerShard[shardIndex] = computed;
        }
        WriteLine($"Captured {capturedPerShard.Count} empty-list computeds on h1");

        // --- Phase 3: start host2; mesh rebalances, h1 loses some shards to h2 ---
        await using var h2 = await NewAppHost(o => o with {
            MeshLockSubspace = "x-liveAudioShardMig",
            MustInitializeDb = false,
        });
        var w2 = h2.Services.GetRequiredService<MeshWatcher>();
        var o2 = h2.Services.ShardOwner(shardScheme);
        await w2.WhenAnnounced;
        WriteLine($"h2 announced: {w2.ThisNode.Ref}");

        // Wait for both nodes to see each other.
        await w1.State.Computed.When(x => x.AllNodes.Count >= 2).WaitAsync(syncTimeout);
        await w2.State.Computed.When(x => x.AllNodes.Count >= 2).WaitAsync(syncTimeout);

        // Wait for shard redistribution: h1 must give up at least one shard to h2.
        await ComputedTest.When(async ct => {
            var bits1 = await o1.BitmapState.Use(ct).ConfigureAwait(false);
            var bits2 = await o2.BitmapState.Use(ct).ConfigureAwait(false);
            WriteLine($"Shard bitmap: h1={bits1.Format()} ({bits1.SetBitCount()}) h2={bits2.Format()} ({bits2.SetBitCount()})");
            bits1.SetBitCount().Should().BeLessThan(shardScheme.ShardCount,
                "h1 must have given up at least one shard to h2");
            (bits1.SetBitCount() + bits2.SetBitCount()).Should().BeGreaterThanOrEqualTo(shardScheme.ShardCount);
        }, syncTimeout);

        // --- Phase 4: identify which shards h1 lost and assert their computeds invalidated ---
        var h1FinalBits = o1.BitmapState.Value;
        var migratedShards = Enumerable.Range(0, shardScheme.ShardCount)
            .Where(i => !h1FinalBits[i])
            .ToList();
        migratedShards.Should().NotBeEmpty();
        WriteLine($"Shards migrated away from h1: [{string.Join(",", migratedShards)}]");

        // For each shard h1 lost, the corresponding captured computed MUST have been
        // invalidated. This is the assertion that fails without the fix: with the empty
        // early-return path and no shard-state dependency, the computed stays consistent
        // forever and WhenInvalidated never completes.
        foreach (var shardIndex in migratedShards) {
            var computed = capturedPerShard[shardIndex];
            var chatId = chatIdPerShard[shardIndex];
            await computed.WhenInvalidated(CancellationToken.None)
                .WaitAsync(syncTimeout);
            computed.IsConsistent().Should().BeFalse(
                $"computed for chat {chatId} (shard {shardIndex}) must invalidate after losing ownership");
        }
    }
}
