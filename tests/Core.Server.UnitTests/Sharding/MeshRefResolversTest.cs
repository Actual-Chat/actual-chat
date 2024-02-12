using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Core.Server.UnitTests.Sharding;

public class MeshRefResolversTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        var nodeA = new NodeRef(Generate.Option);
        var nodeB = new NodeRef(Generate.Option);
        var placeId = new PlaceId(Generate.Option);

        var r1 = MeshRefResolvers.Get<int>()!;
        r1.Invoke(0).Should().Be(MeshRef.Shard(0));
        r1.Invoke(10).Should().Be(MeshRef.Shard(10));

        var r1u = MeshRefResolvers.GetUntyped<int>()!;
        r1u.Invoke(0).Should().Be(MeshRef.Shard(0));
        r1u.Invoke(10).Should().Be(MeshRef.Shard(10));

        var r2 = MeshRefResolvers.Get<int?>()!;
        r2.Invoke(0).Should().Be(MeshRef.Shard(0));
        r2.Invoke(10).Should().Be(MeshRef.Shard(10));
        r2.Invoke(null).Should().Be(MeshRef.Shard(0));

        var r2u = MeshRefResolvers.GetUntyped<int?>()!;
        r2u.Invoke(0).Should().Be(MeshRef.Shard(0));
        r2u.Invoke(10).Should().Be(MeshRef.Shard(10));
        r2u.Invoke(null).Should().Be(MeshRef.Shard(0));

        var r3 = MeshRefResolvers.Get<NodeRef>()!;
        r3.Invoke(nodeA).Should().Be(MeshRef.Node(nodeA));
        r3.Invoke(nodeB).Should().Be(MeshRef.Node(nodeB));

        var r4 = MeshRefResolvers.Get<PlaceId>()!;
        r4.Invoke(placeId).ShardRef.Key.Should().Be(placeId.Value.GetDjb2HashCode());

        var r5 = MeshRefResolvers.Get<TestCommand>()!;
        r5.Invoke(new TestCommand(nodeA)).Should().Be(MeshRef.Node(nodeA));
    }

    public sealed record TestCommand(NodeRef NodeRef) : IHasShardKeySource<NodeRef>
    {
        NodeRef IHasShardKeySource<NodeRef>.GetShardKeySource() => NodeRef;
    }
}
