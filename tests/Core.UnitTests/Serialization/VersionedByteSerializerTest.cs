using System.Buffers;
using ActualChat.Serialization;

namespace ActualChat.Core.UnitTests.Serialization;

public partial class VersionedByteSerializerTest(ITestOutputHelper @out) : TestBase(@out)
{
    // Constructor

    [Fact]
    public void Ctor_RejectsNullSerializers()
    {
        var act = () => new VersionedByteSerializer(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_RejectsEmptySerializers()
    {
        var act = () => new VersionedByteSerializer([]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ctor_RejectsTooManySerializers()
    {
        var fake = FakeByteSerializer.Echo("x");
        var serializers = Enumerable.Repeat<IByteSerializer>(fake, 257).ToArray();
        var act = () => new VersionedByteSerializer(serializers);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ctor_AcceptsExactly256Serializers()
    {
        var fake = FakeByteSerializer.Echo("x");
        var serializers = Enumerable.Repeat<IByteSerializer>(fake, 256).ToArray();
        var s = new VersionedByteSerializer(serializers);
        s.CurrentVersion.Should().Be(255);
    }

    [Fact]
    public void Ctor_CurrentVersionIsLastIndex()
    {
        var fake = FakeByteSerializer.Echo("x");
        new VersionedByteSerializer([fake]).CurrentVersion.Should().Be(0);
        new VersionedByteSerializer([fake, fake]).CurrentVersion.Should().Be(1);
        new VersionedByteSerializer([fake, fake, fake]).CurrentVersion.Should().Be(2);
    }

    // Write

    [Fact]
    public void Write_PrependsCurrentVersionByte_SingleSerializer()
    {
        var s0 = FakeByteSerializer.WritesMarker(0xAA);
        var versioned = new VersionedByteSerializer([s0]);

        var buffer = new ArrayPoolBuffer<byte>();
        versioned.Write(buffer, "ignored", typeof(string));

        buffer.WrittenSpan.ToArray().Should().Equal([0, 0xAA]);
    }

    [Fact]
    public void Write_PrependsCurrentVersionByte_MultipleSerializers()
    {
        var s0 = FakeByteSerializer.WritesMarker(0xAA);
        var s1 = FakeByteSerializer.WritesMarker(0xBB);
        var s2 = FakeByteSerializer.WritesMarker(0xCC);
        var versioned = new VersionedByteSerializer([s0, s1, s2]);

        var buffer = new ArrayPoolBuffer<byte>();
        versioned.Write(buffer, "ignored", typeof(string));

        // Last index = 2; that serializer writes 0xCC.
        buffer.WrittenSpan.ToArray().Should().Equal([2, 0xCC]);
    }

    // Read - happy path

    [Fact]
    public void Read_VersionInRange_UsesMatchingSerializer()
    {
        var s0 = FakeByteSerializer.Echo("v0");
        var s1 = FakeByteSerializer.Echo("v1");
        var versioned = new VersionedByteSerializer([s0, s1]);

        versioned.Read(new byte[] { 0, 0xFF }, typeof(string), out var r0).Should().Be("v0");
        r0.Should().Be(2);

        versioned.Read(new byte[] { 1, 0xFF, 0xEE }, typeof(string), out var r1).Should().Be("v1");
        r1.Should().Be(3);
    }

    [Fact]
    public void Read_VersionOutOfRange_UsesLegacyOnFullData()
    {
        var s0 = FakeByteSerializer.Echo("v0");
        var legacy = FakeByteSerializer.Echo("legacy");
        var versioned = new VersionedByteSerializer([s0], legacy);

        // 0xAB is out of range (only index 0 is valid).
        var data = new byte[] { 0xAB, 0xCD, 0xEF };
        versioned.Read(data, typeof(string), out var read).Should().Be("legacy");
        // Legacy reads the full data (no byte stripped).
        read.Should().Be(3);
    }

    [Fact]
    public void Read_EmptyData_UsesLegacyWhenProvided()
    {
        var s0 = FakeByteSerializer.Echo("v0");
        var legacy = FakeByteSerializer.Echo("legacy");
        var versioned = new VersionedByteSerializer([s0], legacy);

        versioned.Read(ReadOnlyMemory<byte>.Empty, typeof(string), out _).Should().Be("legacy");
    }

    // Read - failure / fallback

    [Fact]
    public void Read_VersionInRange_FallsBackToLegacyOnFailure()
    {
        var s0 = FakeByteSerializer.Throws();
        var legacy = FakeByteSerializer.Echo("legacy");
        var versioned = new VersionedByteSerializer([s0], legacy);

        // First byte is in range (0), but s0 throws -> fall back to legacy on full data.
        var data = new byte[] { 0, 0xFF };
        versioned.Read(data, typeof(string), out var read).Should().Be("legacy");
        read.Should().Be(2);
    }

    [Fact]
    public void Read_VersionInRange_BothFail_RethrowsVersionError()
    {
        var s0 = FakeByteSerializer.Throws("version-error");
        var legacy = FakeByteSerializer.Throws("legacy-error");
        var versioned = new VersionedByteSerializer([s0], legacy);

        // When both serializers fail, the version-based error is the more relevant one
        // and is rethrown; the legacy error is discarded.
        var act = () => versioned.Read(new byte[] { 0, 0xFF }, typeof(string), out _);
        act.Should().Throw<InvalidOperationException>().WithMessage("version-error");
    }

    [Fact]
    public void Read_VersionInRange_NoLegacy_BubblesException()
    {
        var s0 = FakeByteSerializer.Throws();
        var versioned = new VersionedByteSerializer([s0]);

        var act = () => versioned.Read(new byte[] { 0, 0xFF }, typeof(string), out _);
        act.Should().Throw<InvalidOperationException>().WithMessage("forced");
    }

    [Fact]
    public void Read_VersionOutOfRange_NoLegacy_ThrowsWithVersionInMessage()
    {
        var s0 = FakeByteSerializer.Echo("v0");
        var versioned = new VersionedByteSerializer([s0]);

        var act = () => versioned.Read(new byte[] { 0x99 }, typeof(string), out _);
        act.Should().Throw<Exception>().WithMessage("*0x99*out of range*");
    }

    [Fact]
    public void Read_EmptyData_NoLegacy_ThrowsWithEmptyDataMessage()
    {
        var s0 = FakeByteSerializer.Echo("v0");
        var versioned = new VersionedByteSerializer([s0]);

        var act = () => versioned.Read(ReadOnlyMemory<byte>.Empty, typeof(string), out _);
        act.Should().Throw<Exception>().WithMessage("*data is empty*");
    }

    // Round-trip with real serializers

    [Fact]
    public void RoundTrip_RealSerializers_NewFormat()
    {
        var versioned = new VersionedByteSerializer(
            [MessagePackByteSerializer.DefaultTypeDecorating],
            legacy: MemoryPackByteSerializer.DefaultTypeDecorating);

        var value = new TestRecord("hello", 42);
        var buffer = new ArrayPoolBuffer<byte>();
        versioned.Write(buffer, value, typeof(TestRecord));

        // Format byte is 0 (only one serializer in the array).
        buffer.WrittenSpan[0].Should().Be(0);

        var data = buffer.WrittenSpan.ToArray();
        var result = (TestRecord)versioned.Read(data, typeof(TestRecord), out _)!;
        result.Should().Be(value);
    }

    [Fact]
    public void RoundTrip_RealSerializers_LegacyDataReadable()
    {
        // Pre-existing data: written by the legacy serializer with no version byte.
        var legacy = MemoryPackByteSerializer.DefaultTypeDecorating;
        var value = new TestRecord("hello", 42);
        var legacyBuffer = new ArrayPoolBuffer<byte>();
        legacy.Write(legacyBuffer, value, typeof(TestRecord));
        var legacyData = legacyBuffer.WrittenSpan.ToArray();

        // Reader: new format = MessagePack at index 0, legacy fallback = MemoryPack.
        var versioned = new VersionedByteSerializer(
            [MessagePackByteSerializer.DefaultTypeDecorating],
            legacy: legacy);

        var result = (TestRecord)versioned.Read(legacyData, typeof(TestRecord), out _)!;
        result.Should().Be(value);
    }

    // Helpers

    [DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
    public partial record TestRecord(
        [property: DataMember, MemoryPackOrder(0)] string Name,
        [property: DataMember, MemoryPackOrder(1)] int Value);

    private sealed class FakeByteSerializer : IByteSerializer
    {
        public static FakeByteSerializer Echo(object? result)
            => new() { ReadResult = result };
        public static FakeByteSerializer WritesMarker(byte b)
            => new() { WriteMarker = b };
        public static FakeByteSerializer Throws(string message = "forced")
            => new() { Thrower = () => throw new InvalidOperationException(message) };

        public byte? WriteMarker { get; init; }
        public object? ReadResult { get; init; }
        public Action? Thrower { get; init; }

        public object? Read(ReadOnlyMemory<byte> data, Type type, out int readLength)
        {
            Thrower?.Invoke();
            readLength = data.Length;
            return ReadResult;
        }

        public void Write(IBufferWriter<byte> bufferWriter, object? value, Type type)
        {
            var span = bufferWriter.GetSpan(1);
            span[0] = WriteMarker ?? 0;
            bufferWriter.Advance(1);
        }

        public IByteSerializer<T> ToTyped<T>(Type? serializedType = null)
            => throw new NotSupportedException();
    }
}
