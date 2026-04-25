using ActualChat.Serialization.Internal;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Core.UnitTests.Serialization;

public class KeylessMessagePackSerializerTest(ITestOutputHelper @out) : TestBase(@out)
{
    [MessagePackObject]
    public class IntKeyedPerson
    {
        [Key(0)] public string Name { get; set; } = "";
        [Key(1)] public int Age { get; set; }
        [Key(2)] public string? Nickname { get; set; }
    }

    [MessagePackObject]
    public class WithIgnored
    {
        [Key(0)] public int A { get; set; }
        [IgnoreMember] public int Secret { get; set; }
        [Key(1)] public int B { get; set; }
    }

    [MessagePackObject]
    public struct Point
    {
        [Key(0)] public int X;
        [Key(1)] public int Y;
    }

    public class NoContract
    {
        public int A { get; set; }
        public int B { get; set; }
    }

    private static readonly MessagePackSerializerOptions KeylessOptions =
        new(AppMessagePackKeylessResolver.Instance);
    private static readonly MessagePackByteSerializer Default = MessagePackByteSerializer.Default;
    private static readonly MessagePackByteSerializer Keyless = new(KeylessOptions);

    [Fact]
    public void Default_Honors_Key_As_Array()
    {
        var value = new IntKeyedPerson { Name = "Alex", Age = 42, Nickname = "AY" };
        var bytes = Default.Write(value, typeof(IntKeyedPerson)).WrittenSpan.ToArray();
        var json = MessagePackSerializer.ConvertToJson(bytes, MessagePackByteSerializer.DefaultOptions);
        json.Should().Be("[\"Alex\",42,\"AY\"]");
    }

    [Fact]
    public void Keyless_Ignores_Key_Uses_Property_Names()
    {
        var value = new IntKeyedPerson { Name = "Alex", Age = 42, Nickname = "AY" };
        var bytes = Keyless.Write(value, typeof(IntKeyedPerson)).WrittenSpan.ToArray();
        var json = MessagePackSerializer.ConvertToJson(bytes, KeylessOptions);
        json.Should().Be("{\"Name\":\"Alex\",\"Age\":42,\"Nickname\":\"AY\"}");
    }

    [Fact]
    public void Keyless_Roundtrip_Class()
    {
        var value = new IntKeyedPerson { Name = "Alex", Age = 42, Nickname = null };
        var bytes = Keyless.Write(value, typeof(IntKeyedPerson)).WrittenSpan.ToArray();
        var round = (IntKeyedPerson)Keyless.Read(bytes, typeof(IntKeyedPerson), out _)!;
        round.Name.Should().Be("Alex");
        round.Age.Should().Be(42);
        round.Nickname.Should().BeNull();
    }

    [Fact]
    public void Keyless_Roundtrip_Struct()
    {
        var value = new Point { X = 1, Y = -5 };
        var bytes = Keyless.Write(value, typeof(Point)).WrittenSpan.ToArray();
        var json = MessagePackSerializer.ConvertToJson(bytes, KeylessOptions);
        json.Should().Be("{\"X\":1,\"Y\":-5}");

        var round = (Point)Keyless.Read(bytes, typeof(Point), out _)!;
        round.X.Should().Be(1);
        round.Y.Should().Be(-5);
    }

    [Fact]
    public void Keyless_Honors_IgnoreMember()
    {
        var value = new WithIgnored { A = 1, B = 2, Secret = 99 };
        var bytes = Keyless.Write(value, typeof(WithIgnored)).WrittenSpan.ToArray();
        var json = MessagePackSerializer.ConvertToJson(bytes, KeylessOptions);
        json.Should().Be("{\"A\":1,\"B\":2}");

        var round = (WithIgnored)Keyless.Read(bytes, typeof(WithIgnored), out _)!;
        round.A.Should().Be(1);
        round.B.Should().Be(2);
        round.Secret.Should().Be(0);
    }

    [Fact]
    public void Default_And_Keyless_Produce_Different_Bytes()
    {
        var value = new IntKeyedPerson { Name = "x", Age = 1 };
        var defBytes = Default.Write(value, typeof(IntKeyedPerson)).WrittenSpan.ToArray();
        var klBytes = Keyless.Write(value, typeof(IntKeyedPerson)).WrittenSpan.ToArray();

        // Default (Key(N)) starts with an array header; keyless starts with a map header.
        // Array headers: 0x90..0x9F (fixarray); map headers: 0x80..0x8F (fixmap).
        (defBytes[0] & 0xF0).Should().Be(0x90);
        (klBytes[0] & 0xF0).Should().Be(0x80);
    }

    [Fact]
    public void Keyless_Works_With_Primitives()
    {
        var bytes = Keyless.Write(42, typeof(int)).WrittenSpan.ToArray();
        var json = MessagePackSerializer.ConvertToJson(bytes, KeylessOptions);
        json.Should().Be("42");
    }

    [Fact]
    public void Keyless_Works_With_Collections()
    {
        var list = new List<int> { 1, 2, 3 };
        var bytes = Keyless.Write(list, typeof(List<int>)).WrittenSpan.ToArray();
        var json = MessagePackSerializer.ConvertToJson(bytes, KeylessOptions);
        json.Should().Be("[1,2,3]");
    }

    [Fact]
    public void Keyless_Rejects_Types_Without_Contract()
    {
        var act = () => Keyless.Write(new NoContract { A = 1, B = 2 }, typeof(NoContract));
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void RpcHandshake_Roundtrip_Keyed()
    {
        var value = new RpcHandshake(
            RemotePeerId: Guid.Parse("00000000-0000-0000-0000-000000000001"),
            RemoteApiVersionSet: null,
            RemoteHubId: Guid.Parse("00000000-0000-0000-0000-000000000002"),
            ProtocolVersion: RpcHandshake.CurrentProtocolVersion,
            Index: 7);

        var bytes = Default.Write(value, typeof(RpcHandshake)).WrittenSpan.ToArray();
        var json = MessagePackSerializer.ConvertToJson(bytes, MessagePackByteSerializer.DefaultOptions);
        Out.WriteLine($"Keyed wire: {json}");
        // Keyed form: positional array of 5 elements (Key 0..4)
        json.Should().StartWith("[");
        (bytes[0] & 0xF0).Should().Be(0x90); // fixarray header

        var round = (RpcHandshake)Default.Read(bytes, typeof(RpcHandshake), out _)!;
        round.Should().Be(value);
    }

    [Fact]
    public void RpcHandshake_Roundtrip_Keyless()
    {
        var value = new RpcHandshake(
            RemotePeerId: Guid.Parse("00000000-0000-0000-0000-000000000001"),
            RemoteApiVersionSet: null,
            RemoteHubId: Guid.Parse("00000000-0000-0000-0000-000000000002"),
            ProtocolVersion: RpcHandshake.CurrentProtocolVersion,
            Index: 7);

        var bytes = Keyless.Write(value, typeof(RpcHandshake)).WrittenSpan.ToArray();
        var json = MessagePackSerializer.ConvertToJson(bytes, KeylessOptions);
        Out.WriteLine($"Keyless wire: {json}");
        // RpcHandshake lives in ActualLab.Rpc (non-App) — DynamicForceStringKeyResolver skips it,
        // the tail SG/DynamicObject resolvers produce the native [Key(N)] array layout.
        (bytes[0] & 0xF0).Should().Be(0x90); // fixarray header
        json.Should().StartWith("[").And.EndWith("]");

        var round = (RpcHandshake)Keyless.Read(bytes, typeof(RpcHandshake), out _)!;
        round.Should().Be(value);
    }
}
