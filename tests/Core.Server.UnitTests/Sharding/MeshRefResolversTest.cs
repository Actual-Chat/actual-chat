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

        MeshRefResolvers.Get<MeshRefResolversTest>().Should().BeNull();
        MeshRefResolvers.GetUntyped<MeshRefResolversTest>().Should().BeNull();

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

        var r3n = MeshRefResolvers.Get<NodeRef?>()!;
        r3n.Invoke(nodeA).Should().Be(MeshRef.Node(nodeA));
        r3n.Invoke(null).Should().Be(MeshRef.None);

        var r4 = MeshRefResolvers.Get<PlaceId>()!;
        r4.Invoke(placeId).ShardRef.Key.Should().Be(placeId.Value.GetDjb2HashCode());

        var r5 = MeshRefResolvers.Get<TestShardCommand>()!;
        r5.Invoke(new TestShardCommand(10)).Should().Be(MeshRef.Shard(10));
        r5.Invoke(null!).Should().Be(MeshRef.Shard(0));

        var r6 = MeshRefResolvers.Get<TestNodeCommand>()!;
        r6.Invoke(new TestNodeCommand(nodeA)).Should().Be(MeshRef.Node(nodeA));
        r6.Invoke(null!).Should().Be(MeshRef.None);
    }

    public sealed record TestNodeCommand(NodeRef NodeRef) : IHasNodeRef;

    public sealed record TestShardCommand(int Value) : IHasShardKey<int>
    {
        int IHasShardKey<int>.ShardKey => Value;
    }
}
