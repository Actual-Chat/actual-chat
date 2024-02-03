using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Core.Server.UnitTests.Sharding;

public class ValueMeshRefResolversTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        var shardScheme = ShardScheme.Backend.Instance;
        var nodeA = new MeshNodeId(Generate.Option);
        var nodeB = new MeshNodeId(Generate.Option);
        var placeId = new PlaceId(Generate.Option);

        var r1 = ValueMeshRefResolvers.Get<int>()!;
        r1.Invoke(0, shardScheme).Should().Be(MeshRef.Shard(shardScheme, 0));
        r1.Invoke(10, shardScheme).Should().Be(MeshRef.Shard(shardScheme, 10));

        var r1u = ValueMeshRefResolvers.GetUntyped<int>()!;
        r1u.Invoke(0, shardScheme).Should().Be(MeshRef.Shard(shardScheme, 0));
        r1u.Invoke(10, shardScheme).Should().Be(MeshRef.Shard(shardScheme, 10));

        var r2 = ValueMeshRefResolvers.Get<int?>()!;
        r2.Invoke(0, shardScheme).Should().Be(MeshRef.Shard(shardScheme, 0));
        r2.Invoke(10, shardScheme).Should().Be(MeshRef.Shard(shardScheme, 10));
        r2.Invoke(null, shardScheme).Should().Be(MeshRef.Shard(shardScheme, 0));

        var r2u = ValueMeshRefResolvers.GetUntyped<int?>()!;
        r2u.Invoke(0, shardScheme).Should().Be(MeshRef.Shard(shardScheme, 0));
        r2u.Invoke(10, shardScheme).Should().Be(MeshRef.Shard(shardScheme, 10));
        r2u.Invoke(null, shardScheme).Should().Be(MeshRef.Shard(shardScheme, 0));

        var r3 = ValueMeshRefResolvers.Get<MeshNodeId>()!;
        r3.Invoke(nodeA, shardScheme).Should().Be(MeshRef.Node(nodeA));
        r3.Invoke(nodeB, shardScheme).Should().Be(MeshRef.Node(nodeB));

        var r4 = ValueMeshRefResolvers.Get<PlaceId>()!;
        r4.Invoke(placeId, shardScheme).ShardRef.ShardKey.Should().Be(placeId.Value.GetDjb2HashCode());
    }
}
