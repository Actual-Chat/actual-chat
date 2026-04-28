using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Trait("Category", "Slow")]
public class SessionTemporalsShardMigrationTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(SessionTemporalsShardMigrationTest)}", TestAppHostOptions.Default, @out)
{
    private const int MaxHosts = 5;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(20);

    [Fact(Timeout = 180_000)]
    public async Task ShardStealingInvalidatesGetComputed()
    {
        var shardScheme = ShardScheme.UsersBackend;
        var key = "test-key";
        var value = "the-value";

        for (var attempt = 1; attempt <= MaxAttempts; attempt++) {
            var subspace = $"x-tmpShardMig-{attempt}-{Alphabet.AlphaNumeric.Generator8.Next()}";
            var session = Session.New();
            var shardIndex = shardScheme.GetShardIndex(session);
            WriteLine($"Attempt {attempt}: session={session}, target shard={shardIndex}, subspace={subspace}");

            var hosts = new List<TestAppHost>();
            try {
                // Start h1; it owns every UsersBackend shard.
                var h1 = await NewAppHost(o => o with { MeshLockSubspace = subspace });
                hosts.Add(h1);

                var w1 = h1.Services.GetRequiredService<MeshWatcher>();
                var o1 = h1.Services.ShardOwner(shardScheme);
                var s1 = h1.Services.GetRequiredService<ISessionTemporalsBackend>();
                var commander1 = h1.Services.Commander();
                await w1.WhenAnnounced.WaitAsync(SyncTimeout);
                await ComputedTest.When(async ct => {
                    var bits = await o1.BitmapState.Use(ct).ConfigureAwait(false);
                    bits.SetBitCount().Should().Be(shardScheme.ShardCount);
                }, SyncTimeout);

                // Write the value via h1, then capture a Computed for Get on h1.
                await commander1.Call(new SessionTemporalsBackend_Set(session, key, value));
                var c1 = await Computed.Capture(() => s1.Get(session, key, CancellationToken.None));
                c1.Value.Should().Be(value);
                c1.IsConsistent().Should().BeTrue();

                // Add hosts h2..h5 one by one, until shard `shardIndex` migrates off h1.
                var migrated = false;
                for (var i = 2; i <= MaxHosts; i++) {
                    var hN = await NewAppHost(o => o with {
                        MeshLockSubspace = subspace,
                        MustInitializeDb = false,
                    });
                    hosts.Add(hN);

                    // Wait for the new node's announcement and for everyone to see N nodes.
                    var wN = hN.Services.GetRequiredService<MeshWatcher>();
                    await wN.WhenAnnounced.WaitAsync(SyncTimeout);
                    var nodeCount = i;
                    foreach (var h in hosts) {
                        var w = h.Services.GetRequiredService<MeshWatcher>();
                        await w.State.Computed.When(x => x.AllNodes.Count >= nodeCount).WaitAsync(SyncTimeout);
                    }

                    // Wait for the cluster's shard map to stabilize (every shard owned by exactly one node).
                    await ComputedTest.When(async ct => {
                        var sum = 0;
                        foreach (var h in hosts) {
                            var bits = await h.Services.ShardOwner(shardScheme).BitmapState.Use(ct).ConfigureAwait(false);
                            sum += bits.SetBitCount();
                        }
                        sum.Should().Be(shardScheme.ShardCount);
                    }, SyncTimeout);

                    var bits1 = o1.BitmapState.Value;
                    WriteLine($"After +host{i}: h1 bitmap={bits1.Format()} ({bits1.SetBitCount()}), shard {shardIndex} on h1={bits1[shardIndex]}");
                    if (!bits1[shardIndex]) {
                        migrated = true;
                        break;
                    }
                }

                if (!migrated) {
                    WriteLine($"Shard {shardIndex} stayed on h1 after {MaxHosts} hosts; restarting.");
                    continue;
                }

                // The captured Get computed must be invalidated as a result of shard migration,
                // and recomputing it must yield the same value (Redis-backed).
                await c1.WhenInvalidated(CancellationToken.None).WaitAsync(SyncTimeout);
                c1.IsConsistent().Should().BeFalse();

                var c2 = await Computed.Capture(() => s1.Get(session, key, CancellationToken.None));
                c2.Value.Should().Be(value);
                c2.IsConsistent().Should().BeTrue();
                return;
            }
            finally {
                // Dispose in reverse order (h_last first), so that h1 still sees the others leaving.
                for (var i = hosts.Count - 1; i >= 0; i--)
                    await hosts[i].DisposeAsync();
            }
        }

        throw new InvalidOperationException(
            $"Couldn't migrate the target shard off h1 after {MaxAttempts} attempts; " +
            "this is statistically unlikely and likely indicates a sharding regression.");
    }
}
