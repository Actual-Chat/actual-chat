namespace ActualChat.Chat.UnitTests;

public class ChatSerializationTests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ImmutableOptionSetTest1()
    {
        byte[]? lastData = null;
        for (var i = 0; i < 10_000; i++) {
            var items = Enumerable.Range(0, 20).Select(x => x.Format()).Shuffle().ToArray();
            var s = new ImmutableOptionSet();
            foreach (var item in items)
                s = s.Set(item, item);
            var data = MemoryPackByteSerializer.Default.Write(s).WrittenSpan.ToArray();
            if (lastData != null)
                data.SequenceEqual(lastData).Should().BeTrue();
            lastData = data;
        }
    }

    [Fact(Skip = "Super slow, run it manually")]
    public void ImmutableOptionSetTest2()
    {
        var s1 = "a";
        var s2 = FindSameHashedString(s1);
        if (s2 == null)
            return;

        Out.WriteLine($"Same hashed strings: '{s1}', '{s2}'");
        var s = new ImmutableOptionSet();
        byte[]? lastData = null;
        for (var i = 0; i < 2; i++) {
            var items = i == 0 ? (string[])[s1, s2] : [s2, s1];
            foreach (var item in items)
                s = s.Set(item, item);
            var data = MemoryPackByteSerializer.Default.Write(s).WrittenSpan.ToArray();
            if (lastData != null)
                data.SequenceEqual(lastData).Should().BeTrue();
            lastData = data;
        }
    }

    [Fact]
    public void LegacyLastCheckInTest()
    {
        TestLegacyNullable(1L, 16);
        TestLegacyNullable(Moment.Now, 16);
    }

    [Fact]
    // [Fact(Skip = "Investigate why it fails on GitHub")]
    public void LegacyTileReadTest()
    {
        AssertCorrectSize<Moment?>(16);

        using var stream1 = File.OpenRead("data\\get-tile1.bin");
        using var stream2 = File.OpenRead("data\\get-tile2.bin");

        var tile1Bytes = ReadAsArray(stream1)[8..];
        var tile2Bytes = ReadAsArray(stream2)[8..];

        var tile1 = MemoryPackByteSerializer.Default.Read<ChatTile>(tile1Bytes, out var readLength1);
        readLength1.Should().Be(tile1Bytes.Length);

        var tile2 = MemoryPackByteSerializer.Default.Read<ChatTile>(tile2Bytes, out var readLength2);
        readLength2.Should().Be(tile2Bytes.Length);

        tile1.Should().BeEquivalentTo(tile2);
        tile1.Entries.Count.Should().Be(tile2.Entries.Count);
        tile1.Entries[0].Should().BeEquivalentTo(tile2.Entries[0]);

        var moment1 = tile1.Entries[0].ClientSideBeginsAt;
        var moment2 = tile2.Entries[0].ClientSideBeginsAt;
        moment2.Should().Be(moment1);

        var bytes1 = MemoryPackByteSerializer.Default.Write(moment1.ToApiNullable()).WrittenSpan.ToArray();
        var bytes2 = MemoryPackByteSerializer.Default.Write(moment2.ToApiNullable()).WrittenSpan.ToArray();
        bytes2.Should().Equal(bytes1);

        bytes1 = MemoryPackByteSerializer.Default.Write(moment1.ToApiNullable8()).WrittenSpan.ToArray();
        bytes2 = MemoryPackByteSerializer.Default.Write(moment2.ToApiNullable8()).WrittenSpan.ToArray();
        bytes2.Should().Equal(bytes1);

        bytes1 = MemoryPackByteSerializer.Default.Write(tile1).WrittenSpan.ToArray();
        bytes2 = MemoryPackByteSerializer.Default.Write(tile2).WrittenSpan.ToArray();
        bytes2.Should().Equal(bytes1);

        // var entry1Serialized = MemoryPackSerialized.New(tile1.Entries[0]);
        // var entry2Serialized = MemoryPackSerialized.New(tile2.Entries[0]);
        // var entry1Bytes = entry1Serialized.Data;
        // var entry2Bytes = entry2Serialized.Data;
        // entry1Bytes.Should().BeEquivalentTo(entry2Bytes);

        // var tile1Serialized2 = MemoryPackSerialized.New(tile1);
        // var tile2Serialized2 = MemoryPackSerialized.New(tile2);
        // var tile1Bytes2 = tile1Serialized2.Data;
        // var tile2Bytes2 = tile2Serialized2.Data;
        //
        // tile1Bytes2.Should().BeEquivalentTo(tile2Bytes2);
        // tile1Bytes2.Should().BeEquivalentTo(tile1Bytes2);
    }

    // Private methods

    private void TestLegacyNullable<T>(T value, int expectedSize)
        where T : unmanaged
    {
        AssertCorrectSize<T?>(expectedSize);

        var m0 = MemoryPackSerialized.New<T?>(value);
        var c0 = MemoryPackSerialized.New<ApiNullable8<T>>(m0.Data);
        c0.Value.Should().Be(value);
        var m1 = MemoryPackSerialized.New<T?>(c0.Data);
        m1.Value.Should().Be(value);

        m0 = MemoryPackSerialized.New((T?)null);
        c0 = MemoryPackSerialized.New<ApiNullable8<T>>(m0.Data);
        c0.Value.Value.Should().BeNull();
        m1 = MemoryPackSerialized.New<T?>(c0.Data);
        m1.Value.Should().BeNull();
    }

    private void AssertCorrectSize<T>(int expectedSize)
    {
        var size = Unsafe.SizeOf<T>();
        if (size == expectedSize)
            return;

        Out.WriteLine($"Size of {typeof(T).GetName()} = {size} (expected: {expectedSize})");
        size.Should().Be(expectedSize);
    }

    private static string? FindSameHashedString(string s)
    {
        var h = s.GetHashCode(StringComparison.Ordinal);
        for (var i = 0L; i < (long)int.MaxValue * 2; i++) {
            var s1 = i.Format();
            if (s1.GetHashCode(StringComparison.Ordinal) == h)
                return s1;
        }
        return null;
    }

    private static byte[] ReadAsArray(Stream input)
    {
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        return ms.ToArray();
    }
}
