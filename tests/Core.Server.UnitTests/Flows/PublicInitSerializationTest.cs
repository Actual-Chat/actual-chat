using ActualLab.Testing;

namespace ActualChat.Core.Server.UnitTests.Flows;

// Verifies whether MessagePack-CSharp can write to public `init` setters
// without [SerializationConstructor] and without AllowPrivate=true.
public partial class PublicInitSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void MessagePack_PublicInit_RoundTrips()
    {
        var original = new TypeWithPublicInit {
            Number = 42,
            Text = "hello",
        };
        var roundTripped = original.PassThroughMessagePackByteSerializer(Out);
        roundTripped.Number.Should().Be(42);
        roundTripped.Text.Should().Be("hello");
    }

    [Fact]
    public void MessagePack_PublicInit_WithDefaultCtor_RoundTrips()
    {
        var original = new TypeWithPublicInitAndDefaultCtor {
            Number = 42,
            Text = "hello",
        };
        var roundTripped = original.PassThroughMessagePackByteSerializer(Out);
        roundTripped.Number.Should().Be(42);
        roundTripped.Text.Should().Be("hello");
    }

    [DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
    public partial class TypeWithPublicInit
    {
        [DataMember, MemoryPackOrder(0)] public int Number { get; init; }
        [DataMember, MemoryPackOrder(1)] public string Text { get; init; } = "";
    }

    [DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
    public partial class TypeWithPublicInitAndDefaultCtor
    {
        public TypeWithPublicInitAndDefaultCtor() { }

        [DataMember, MemoryPackOrder(0)] public int Number { get; init; }
        [DataMember, MemoryPackOrder(1)] public string Text { get; init; } = "";
    }
}
