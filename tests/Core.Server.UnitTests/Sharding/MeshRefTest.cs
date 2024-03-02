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
        r.Should().Be(new ShardRef(null!, 0));
        r.Should().Be(new ShardRef(ShardScheme.None, 0));
        r.Should().NotBe(new ShardRef(ShardScheme.None, 1));
        r.Should().NotBe(new ShardRef(ShardScheme.Undefined, 0));
        r.IsNone.Should().BeTrue();
        r.IsValid.Should().BeFalse();
        r.Scheme.IsNone.Should().BeTrue();
        r.Scheme.IsUndefined.Should().BeFalse();
        r.Key.Should().Be(0);
        r.WithSchemeIfUndefined(ShardScheme.AnyServer).IsNone.Should().BeTrue();
        r.TryGetShardIndex().Should().BeNull();
        Assert.Throws<ArgumentOutOfRangeException>(() => r.GetShardIndex());

        r = new ShardRef(0);
        r.Should().Be(new ShardRef(ShardScheme.Undefined, 0));
        r.Should().NotBe(default(ShardRef));
        r.IsNone.Should().BeFalse();
        r.IsValid.Should().BeFalse();
        r.Scheme.IsNone.Should().BeFalse();
        r.Scheme.IsUndefined.Should().BeTrue();
        r.Key.Should().Be(0);
        r.TryGetShardIndex().Should().BeNull();

        r = r.WithSchemeIfUndefined(ShardScheme.AnyServer);
        r.Should().Be(new ShardRef(ShardScheme.AnyServer, 0));

        r = new ShardRef(1);
        r.Should().Be(new ShardRef(ShardScheme.Undefined, 1));
        r.Should().NotBe(default(ShardRef));
        r.IsNone.Should().BeFalse();
        r.IsValid.Should().BeFalse();
        r.Scheme.IsUndefined.Should().BeTrue();
        r.Key.Should().Be(1);
        r.TryGetShardIndex().Should().BeNull();

        r = r.WithSchemeIfUndefined(ShardScheme.AnyServer);
        r.Should().Be(new ShardRef(ShardScheme.AnyServer, 1));
        r.IsNone.Should().BeFalse();
        r.IsValid.Should().BeTrue();
        r.Scheme.IsUndefined.Should().BeFalse();
        r.Scheme.Should().BeSameAs(ShardScheme.AnyServer);
        r.Key.Should().Be(1);
        r.TryGetShardIndex().Should().Be(1);
        r.GetShardIndex().Should().Be(1);

        r = new ShardRef(ShardScheme.AnyServer, ShardScheme.AnyServer.ShardCount + 1);
        r.IsValid.Should().BeTrue();
        r.Key.Should().NotBe(1);
        r.TryGetShardIndex().Should().Be(1);
        r.GetShardIndex().Should().Be(1);

        var rn = r.Normalize();
        rn.Key.Should().Be(1);

        r.WithSchemeIfUndefined(ShardScheme.None).Should().Be(r);
        rn.WithSchemeIfUndefined(ShardScheme.Undefined).Should().Be(rn);
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
        r.Should().Be(MeshRef.Shard(ShardScheme.Undefined, 1));
        r.Should().NotBe(MeshRef.Shard(0));
        r.IsNone.Should().BeFalse();
        r.NodeRef.IsNone.Should().BeTrue();
        r.ShardRef.IsNone.Should().BeFalse();
        r.ShardRef.IsValid.Should().BeFalse();
        r.ShardRef.Key.Should().Be(1);
        r.ShardRef.TryGetShardIndex().Should().Be(null);
        Assert.Throws<ArgumentOutOfRangeException>(() => r.ShardRef.GetShardIndex().Should().Be(1));

        r = r.WithSchemeIfUndefined(ShardScheme.AnyServer);
        r.Should().Be(MeshRef.Shard(ShardScheme.AnyServer, 1));
        r.ShardRef.Scheme.IsUndefined.Should().BeFalse();
        r.ShardRef.Scheme.Should().BeSameAs(ShardScheme.AnyServer);
        r.ShardRef.Key.Should().Be(1);
        r.ShardRef.GetShardIndex().Should().Be(1);

        r = MeshRef.Shard(ShardScheme.AnyServer, ShardScheme.AnyServer.ShardCount + 1);
        r.ShardRef.GetShardIndex().Should().Be(1);
        r.Normalize().ShardRef.Key.Should().Be(1);
        r = r.WithSchemeIfUndefined(ShardScheme.Undefined);
        r.ShardRef.Scheme.Should().BeSameAs(ShardScheme.AnyServer);
        r.ShardRef.Key.Should().Be(ShardScheme.AnyServer.ShardCount + 1);
        r.ShardRef.TryGetShardIndex().Should().Be(1);
        r.ShardRef.GetShardIndex().Should().Be(1);

        var rn = r.Normalize();
        rn.ShardRef.Key.Should().Be(1);

        r.WithSchemeIfUndefined(ShardScheme.None).Should().Be(r);
        rn.WithSchemeIfUndefined(ShardScheme.Undefined).Should().Be(rn);
    }
}
