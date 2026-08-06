namespace ActualChat.Core.Server.IntegrationTests.Sharding;

public class ShardMapTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void SmallNodeCountTest()
        => Test(4);

    [Fact]
    public void LargeNodeCountTest()
        => Test(20);

    [Fact]
    public void ReallocationTest()
    {
        var nodeRoles = HostRoles.Server.GetAllRoles(HostRole.OneBackendServer);
        var shardScheme = ShardScheme.TestBackend;
        var shardCount = shardScheme.ShardCount;

        MeshNode MakeNode(int id) => new(new NodeRef($"node-{id}"), "local:80", nodeRoles, MeshNodeState.Online);

        // Test: removing a node should only reallocate ~shardCount/nodeCount shards
        var nodes3 = new[] { MakeNode(1), MakeNode(2), MakeNode(3) };
        var nodes2 = new[] { MakeNode(1), MakeNode(2) }; // node-3 dies

        var map3 = new ShardMap(shardScheme, nodes3);
        var map2 = new ShardMap(shardScheme, nodes2);

        var reallocated = 0;
        var node3Shards = 0;
        for (var s = 0; s < shardCount; s++) {
            var owner3 = map3[s]?.Ref.Id.Value;
            var owner2 = map2[s]?.Ref.Id.Value;
            if (owner3 == "node-3")
                node3Shards++;
            else if (owner3 != owner2)
                reallocated++;
        }

        WriteLine($"Shards on dead node: {node3Shards}");
        WriteLine($"Extra reallocations (beyond dead node's shards): {reallocated}");
        WriteLine($"Map with 3 nodes:\n{map3}");
        WriteLine($"Map with 2 nodes:\n{map2}");

        // With rendezvous hashing, extra reallocations should be very small (0-2 from rebalancing)
        reallocated.Should().BeInRange(0, 2,
            "rendezvous hashing should cause minimal extra reallocations beyond the dead node's shards");
    }

    // Private methods

    private void Test(int averageNodeCount)
    {
        var nodeRoles = HostRoles.Server.GetAllRoles(HostRole.OneBackendServer);
        var rnd = new Random(15 + averageNodeCount);
        var nodes = new List<MeshNode>();
        for (var i = 0; i < 200; i++) {
            var mustAdd = nodes.Count == 0 || rnd.Next(nodes.Count + averageNodeCount) > nodes.Count;
            if (mustAdd)
                nodes.Add(new MeshNode(new NodeRef($"node-{i}"), "local:80", nodeRoles, MeshNodeState.Online));
            else
                nodes.RemoveAt(rnd.Next(nodes.Count));
            var shardMap = new ShardMap(ShardScheme.TestBackend, [..nodes]);
            if (!shardMap.IsEmpty) {
                var nodeIndexes = shardMap.NodeIndexes;
                nodeIndexes.All(x => x.HasValue).Should().BeTrue();
                var shardGroups = nodeIndexes.GroupBy(x => x).ToArray();
                shardGroups.Length.Should().Be(Math.Min(nodes.Count, shardMap.NodeIndexes.Length));
                var minCount = shardGroups.Min(g => g.Count());
                var maxCount = shardGroups.Max(g => g.Count());
                var delta = maxCount - minCount;
                delta.Should().BeInRange(0, 1);
            }
            WriteLine(shardMap.ToString());
        }
    }
}
