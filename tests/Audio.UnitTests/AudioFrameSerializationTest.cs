using System.Buffers;
using MessagePack;

namespace ActualChat.Audio.UnitTests;

public class AudioFrameSerializationTest
{
    private readonly ITestOutputHelper _out;

    public AudioFrameSerializationTest(ITestOutputHelper @out)
        => _out = @out;

    [Fact]
    public async Task SerializeTest()
    {
        var arrayBufferWriter = new ArrayBufferWriter<byte>(4096);
        // var writer = new MessagePackWriter(arrayBufferWriter);
        // writer.Write();
        var a = new AudioFrame { Data = new byte[] { 1, 2, 3 }, Offset = TimeSpan.FromMilliseconds(20)};
        var b = new AudioFrame { Data = new byte[] { 5, 6, 7 } };
        MessagePackSerializer.Serialize(arrayBufferWriter, a, MessagePackSerializerOptions.Standard);
        MessagePackSerializer.Serialize(arrayBufferWriter, b, MessagePackSerializerOptions.Standard);

        _out.WriteLine("[{0}]", string.Join(", ", arrayBufferWriter.WrittenMemory.ToArray()));
        var x = MessagePackSerializer.Deserialize<AudioFrame>(arrayBufferWriter.WrittenMemory, MessagePackSerializerOptions.Standard);
        x.Should().BeEquivalentTo(a);
    }
}
