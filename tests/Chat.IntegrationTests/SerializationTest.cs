
namespace ActualChat.Chat.IntegrationTests;

public class SerializationTest
{
    [Fact]
    public async Task LegacyTileReadTest()
    {
        await using var stream1 = File.OpenRead("data\\get-tile1.bin");
        await using var stream2 = File.OpenRead("data\\get-tile2.bin");
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
        moment1.Should().Be(moment2);
 #pragma warning disable CS0618 // Type or member is obsolete
        moment1.RawHasValue.Should().NotBe(moment2.RawHasValue);
 #pragma warning restore CS0618 // Type or member is obsolete

        moment1 = moment1.Value;
        moment2 = moment2.Value;
        var moment1Bytes = MemoryPackByteSerializer.Default.Write(moment1).WrittenSpan.ToArray();
        var moment2Bytes = MemoryPackByteSerializer.Default.Write(moment2).WrittenSpan.ToArray();
        moment1Bytes.Should().Equal(moment2Bytes);

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

    private static byte[] ReadAsArray(Stream input)
    {
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        return ms.ToArray();
    }
}
