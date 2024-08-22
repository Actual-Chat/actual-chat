
namespace ActualChat.Chat.IntegrationTests;

public class SerializationTest
{
    [Fact(Skip = "Does not work yet")]
    public async Task ReadTile()
    {
        await using var stream1 = File.OpenRead("data\\get-tile1.bin");
        await using var stream2 = File.OpenRead("data\\get-tile2.bin");

        var tile1Bytes = ReadAsArray(stream1)[8..];
        var tile2Bytes = ReadAsArray(stream2)[8..];
        var tile1Serialized = MemoryPackSerialized.New<ChatTile>(tile1Bytes);
        var tile2Serialized = MemoryPackSerialized.New<ChatTile>(tile2Bytes);
        var tile1 = tile1Serialized.Value;
        var tile2 = tile2Serialized.Value;
        tile1.Should().BeEquivalentTo(tile2);
        tile1.Entries.Count.Should().Be(tile2.Entries.Count);
        tile1.Entries[0].Should().BeEquivalentTo(tile2.Entries[0]);

        var moment1 = tile1.Entries[0].ClientSideBeginsAt;
        var moment2 = tile2.Entries[0].ClientSideBeginsAt;
        moment1.Should().BeEquivalentTo(moment2);

        var moment1Serialized = MemoryPackSerialized.New(tile1.Entries[0].ClientSideBeginsAt);
        var moment2Serialized = MemoryPackSerialized.New(tile2.Entries[0].ClientSideBeginsAt);
        var moment1Bytes = moment1Serialized.Data;
        var moment2Bytes = moment2Serialized.Data;
        moment1Bytes.Should().BeEquivalentTo(moment2Bytes);

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
