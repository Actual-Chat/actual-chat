namespace ActualChat.Core.Server.UnitTests.Sharding;

public class MeshRefTest(ITestOutputHelper @out) : TestBase(@out)
{
    private ShardScheme NoneScheme => ShardScheme.None;
    private ShardScheme DefaultScheme => ShardScheme.Default;
    private ShardScheme AnyServerScheme => ShardScheme.AnyServer;

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
        r.Should().Be(new ShardRef(NoneScheme, 0));
        r.Should().NotBe(new ShardRef(NoneScheme, 1));
        r.Should().NotBe(new ShardRef(DefaultScheme, 0));
        r.IsNone.Should().BeTrue();
        r.Scheme.IsNone.Should().BeTrue();
        r.Scheme.IsDefault.Should().BeFalse();
        r.Key.Should().Be(0);
        r.Index.Should().Be(-1);

        r = new ShardRef(1);
        r.Should().Be(new ShardRef(DefaultScheme, 1));
        r.Should().NotBe(default(ShardRef));
        r.IsNone.Should().BeFalse();
        r.Scheme.IsNone.Should().BeFalse();
        r.Scheme.IsDefault.Should().BeTrue();
        r.Key.Should().Be(1);
        r.Index.Should().Be(-1);

        r = r.WithNonDefaultSchemeOr(AnyServerScheme);
        r.Should().Be(new ShardRef(AnyServerScheme, 1));
        r.IsNone.Should().BeFalse();
        r.Scheme.IsNone.Should().BeFalse();
        r.Scheme.IsDefault.Should().BeFalse();
        r.Scheme.Should().BeSameAs(AnyServerScheme);
        r.Key.Should().Be(1);
        r.Index.Should().Be(r.Scheme.GetShardIndex(r.Key));

        r = new ShardRef(AnyServerScheme, AnyServerScheme.ShardCount + 1);
        r.Key.Should().NotBe(1);
        r.Index.Should().Be(1);
        r.Normalize().Key.Should().Be(1);
        r = r.WithNonDefaultSchemeOr(NoneScheme, true);
        r.Scheme.Should().BeSameAs(AnyServerScheme);
        r.Key.Should().Be(1);
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
        r.Should().Be(MeshRef.Shard(DefaultScheme, 1));
        r.Should().NotBe(MeshRef.Shard(0));
        r.IsNone.Should().BeFalse();
        r.NodeRef.IsNone.Should().BeTrue();
        r.ShardRef.IsNone.Should().BeFalse();
        r.ShardRef.Scheme.IsDefault.Should().BeTrue();
        r.ShardRef.Key.Should().Be(1);
        r.ShardRef.Index.Should().Be(r.ShardRef.Scheme.GetShardIndex(r.ShardRef.Key));

        r = r.WithNonDefaultSchemeOr(AnyServerScheme);
        r.Should().Be(MeshRef.Shard(AnyServerScheme, 1));
        r.ShardRef.Scheme.IsDefault.Should().BeFalse();
        r.ShardRef.Scheme.Should().BeSameAs(AnyServerScheme);
        r.ShardRef.Key.Should().Be(1);
        r.ShardRef.Index.Should().Be(r.ShardRef.Scheme.GetShardIndex(r.ShardRef.Key));

        r = MeshRef.Shard(AnyServerScheme, AnyServerScheme.ShardCount + 1);
        r.ShardRef.Index.Should().Be(1);
        r.Normalize().ShardRef.Key.Should().Be(1);
        r = r.WithNonDefaultSchemeOr(NoneScheme, true);
        r.ShardRef.Scheme.Should().BeSameAs(AnyServerScheme);
        r.ShardRef.Key.Should().Be(1);
    }
}
