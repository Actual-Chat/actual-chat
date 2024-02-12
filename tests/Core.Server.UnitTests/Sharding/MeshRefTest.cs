namespace ActualChat.Core.Server.UnitTests.Sharding;

public class MeshRefTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void NodeRefTest()
    {
        var r = default(NodeRef);
        r.IsNone.Should().BeTrue();
        r.AssertPassesThroughAllSerializers();

        r = new NodeRef(Generate.Option);
        r.IsNone.Should().BeFalse();
        r.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void ShardRefTest()
    {
        var r = default(ShardRef);
        r.Should().Be(new ShardRef(ShardScheme.None.Instance, 0));
        r.Should().NotBe(new ShardRef(ShardScheme.None.Instance, 1));
        r.Should().NotBe(new ShardRef(ShardScheme.Undefined.Instance, 0));
        r.IsNone.Should().BeTrue();
        r.Scheme.IsNone.Should().BeTrue();
        r.Scheme.IsUndefined.Should().BeFalse();
        r.Key.Should().Be(0);
        r.Index.Should().Be(-1);

        r = new ShardRef(1);
        r.Should().Be(new ShardRef(ShardScheme.Undefined.Instance, 1));
        r.Should().NotBe(default(ShardRef));
        r.IsNone.Should().BeFalse();
        r.Scheme.IsNone.Should().BeFalse();
        r.Scheme.IsUndefined.Should().BeTrue();
        r.Key.Should().Be(1);
        r.Index.Should().Be(-1);

        r = r.WithSchemeIfUndefined(ShardScheme.Backend.Instance);
        r.Should().Be(new ShardRef(ShardScheme.Backend.Instance, 1));
        r.IsNone.Should().BeFalse();
        r.Scheme.IsNone.Should().BeFalse();
        r.Scheme.IsUndefined.Should().BeFalse();
        r.Scheme.Should().BeSameAs(ShardScheme.Backend.Instance);
        r.Key.Should().Be(1);
        r.Index.Should().Be(r.Scheme.GetShardIndex(r.Key));
    }

    [Fact]
    public void BasicTest()
    {
        var r = default(MeshRef);
        r.IsNone.Should().BeTrue();
        r.NodeRef.IsNone.Should().BeTrue();
        r.ShardRef.IsNone.Should().BeTrue();

        r = MeshRef.Shard(1);
        r.Should().Be(MeshRef.Shard(1));
        r.Should().Be(MeshRef.Shard(ShardScheme.Undefined.Instance, 1));
        r.Should().NotBe(MeshRef.Shard(0));
        r.IsNone.Should().BeFalse();
        r.NodeRef.IsNone.Should().BeTrue();
        r.ShardRef.IsNone.Should().BeFalse();
        r.ShardRef.Scheme.IsUndefined.Should().BeTrue();
        r.ShardRef.Key.Should().Be(1);
        r.ShardRef.Index.Should().Be(r.ShardRef.Scheme.GetShardIndex(r.ShardRef.Key));

        r = r.WithSchemeIfUndefined(ShardScheme.Backend.Instance);
        r.Should().Be(MeshRef.Shard(ShardScheme.Backend.Instance, 1));
        r.ShardRef.Scheme.IsUndefined.Should().BeFalse();
        r.ShardRef.Scheme.Should().BeSameAs(ShardScheme.Backend.Instance);
        r.ShardRef.Key.Should().Be(1);
        r.ShardRef.Index.Should().Be(r.ShardRef.Scheme.GetShardIndex(r.ShardRef.Key));
    }
}
