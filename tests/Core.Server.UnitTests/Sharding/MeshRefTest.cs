namespace ActualChat.Core.Server.UnitTests.Sharding;

public class MeshRefTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void NodeRefTest()
    {
        var r = default(NodeRef);
        r.IsNone.Should().BeTrue();
        r.AssertPassesThroughSerializers();

        r = new NodeRef(Generate.Option);
        r.IsNone.Should().BeFalse();
        r.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ShardRefTest()
    {
        var r = default(ShardRef);
        r.Should().Be(new ShardRef(null!, ShardKey.New(0)));
        r.Should().Be(new ShardRef(ShardScheme.None, ShardKey.New(0)));
        r.Should().NotBe(new ShardRef(ShardScheme.None, ShardKey.New(1)));
        r.Should().NotBe(new ShardRef(ShardScheme.Undefined, ShardKey.New(0)));
        r.IsNone.Should().BeTrue();
        r.IsValid.Should().BeFalse();
        r.Scheme.IsNone.Should().BeTrue();
        r.Scheme.IsUndefined.Should().BeFalse();
        r.Key.Should().Be(ShardKey.New(0));
        r.WithSchemeIfUndefined(ShardScheme.TestBackend).IsNone.Should().BeTrue();
        r.TryGetShardIndex().Should().BeNull();
        Assert.Throws<ArgumentOutOfRangeException>(() => r.GetShardIndex());

        r = new ShardRef(ShardKey.New(0));
        r.Should().Be(new ShardRef(ShardScheme.Undefined, ShardKey.New(0)));
        r.Should().NotBe(default(ShardRef));
        r.IsNone.Should().BeFalse();
        r.IsValid.Should().BeFalse();
        r.Scheme.IsNone.Should().BeFalse();
        r.Scheme.IsUndefined.Should().BeTrue();
        r.Key.Should().Be(ShardKey.New(0));
        r.TryGetShardIndex().Should().BeNull();

        r = r.WithSchemeIfUndefined(ShardScheme.TestBackend);
        r.Should().Be(new ShardRef(ShardScheme.TestBackend, ShardKey.New(0)));

        r = new ShardRef(ShardKey.New(1));
        r.Should().Be(new ShardRef(ShardScheme.Undefined, ShardKey.New(1)));
        r.Should().NotBe(default(ShardRef));
        r.IsNone.Should().BeFalse();
        r.IsValid.Should().BeFalse();
        r.Scheme.IsUndefined.Should().BeTrue();
        r.Key.Should().Be(ShardKey.New(1));
        r.TryGetShardIndex().Should().BeNull();

        r = r.WithSchemeIfUndefined(ShardScheme.TestBackend);
        r.Should().Be(new ShardRef(ShardScheme.TestBackend, ShardKey.New(1)));
        r.IsNone.Should().BeFalse();
        r.IsValid.Should().BeTrue();
        r.Scheme.IsUndefined.Should().BeFalse();
        r.Scheme.Should().BeSameAs(ShardScheme.TestBackend);
        r.Key.Should().Be(ShardKey.New(1));
        r.TryGetShardIndex().Should().Be(1);
        r.GetShardIndex().Should().Be(1);

        r = new ShardRef(ShardScheme.TestBackend, ShardKey.New(ShardScheme.TestBackend.ShardCount + 1));
        r.IsValid.Should().BeTrue();
        r.Key.Should().NotBe(ShardKey.New(1));
        r.TryGetShardIndex().Should().Be(1);
        r.GetShardIndex().Should().Be(1);

        var rn = r.Normalize();
        rn.Key.Should().Be(ShardKey.New(1));

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

        r = MeshRef.Shard(ShardKey.New(1));
        r.Should().Be(MeshRef.Shard(ShardKey.New(1)));
        r.Should().Be(MeshRef.Shard(ShardScheme.Undefined, ShardKey.New(1)));
        r.Should().NotBe(MeshRef.Shard(ShardKey.New(0)));
        r.IsNone.Should().BeFalse();
        r.NodeRef.IsNone.Should().BeTrue();
        r.ShardRef.IsNone.Should().BeFalse();
        r.ShardRef.IsValid.Should().BeFalse();
        r.ShardRef.Key.Should().Be(ShardKey.New(1));
        r.ShardRef.TryGetShardIndex().Should().Be(null);
        Assert.Throws<ArgumentOutOfRangeException>(() => r.ShardRef.GetShardIndex().Should().Be(1));

        r = r.WithSchemeIfUndefined(ShardScheme.TestBackend);
        r.Should().Be(MeshRef.Shard(ShardScheme.TestBackend, ShardKey.New(1)));
        r.ShardRef.Scheme.IsUndefined.Should().BeFalse();
        r.ShardRef.Scheme.Should().BeSameAs(ShardScheme.TestBackend);
        r.ShardRef.Key.Should().Be(ShardKey.New(1));
        r.ShardRef.GetShardIndex().Should().Be(1);

        r = MeshRef.Shard(ShardScheme.TestBackend, ShardKey.New(ShardScheme.TestBackend.ShardCount + 1));
        r.ShardRef.GetShardIndex().Should().Be(1);
        r.Normalize().ShardRef.Key.Should().Be(ShardKey.New(1));
        r = r.WithSchemeIfUndefined(ShardScheme.Undefined);
        r.ShardRef.Scheme.Should().BeSameAs(ShardScheme.TestBackend);
        r.ShardRef.Key.Should().Be(ShardKey.New(ShardScheme.TestBackend.ShardCount + 1));
        r.ShardRef.TryGetShardIndex().Should().Be(1);
        r.ShardRef.GetShardIndex().Should().Be(1);

        var rn = r.Normalize();
        rn.ShardRef.Key.Should().Be(ShardKey.New(1));

        r.WithSchemeIfUndefined(ShardScheme.None).Should().Be(r);
        rn.WithSchemeIfUndefined(ShardScheme.Undefined).Should().Be(rn);
    }
}
