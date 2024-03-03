using ActualChat.Hosting;
using ActualChat.Mesh;

namespace ActualChat.Core.Server.IntegrationTests;

public class ShardMapTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void SmallNodeCountTest()
        => Test(4);

    [Fact]
    public void LargeNodeCountTest()
        => Test(20);

    private void Test(int averageNodeCount)
    {
        var nodeRoles = HostRoles.Server.GetAllRoles(HostRole.OneBackendServer);
        var rnd = new Random(15 + averageNodeCount);
        var nodes = new List<MeshNode>();
        for (var i = 0; i < 200; i++) {
            var mustAdd = nodes.Count == 0 || rnd.Next(nodes.Count + averageNodeCount) > nodes.Count;
            if (mustAdd)
                nodes.Add(new MeshNode(new NodeRef($"node-{i}"), "local:80", nodeRoles));
            else
                nodes.RemoveAt(rnd.Next(nodes.Count));
            var shardMap = new ShardMap(ShardScheme.AnyServer, nodes.ToImmutableArray());
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
            Out.WriteLine(shardMap.ToString());
        }
    }
}
