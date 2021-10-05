using System.Buffers;
using System.Threading.Channels;
using ActualChat.Blobs;
using Grpc.Core;
using Stl.Time;
using Channel = System.Threading.Channels.Channel;

namespace ActualChat.Audio.UnitTests;


public class MediaFrameExtractorTest
{
    [Fact]
    public async Task ExtractFramesFromFile()
    {
        var audioSourceProvider = new AudioSourceProvider();
        var blobChannel = Channel.CreateUnbounded<BlobPart>();
        var audioSourceTask = audioSourceProvider.ExtractMediaSource(blobChannel.Reader, CancellationToken.None);

        _ = ReadFromFile(blobChannel.Writer);

        var audioSource = await audioSourceTask;

        var offset = TimeSpan.Zero;
        await foreach (var audioFrame in audioSource)
            offset = audioFrame.Offset > offset
                ? audioFrame.Offset
                : offset;

        offset.Should().Be(TimeSpan.FromMilliseconds(12120));
    }


    private static async Task<int> ReadFromFile(ChannelWriter<BlobPart> writer)
    {
        var size = 0;
        await using var inputStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, @"data", "file.webm"),
            FileMode.Open,
            FileAccess.Read);
        using var readBufferLease = MemoryPool<byte>.Shared.Rent(1 * 1024);
        var readBuffer = readBufferLease.Memory;
        var index = 0;
        var bytesRead = await inputStream.ReadAsync(readBuffer);
        size += bytesRead;
        while (bytesRead > 0) {
            var command = new BlobPart(
                index++,
                readBuffer[..bytesRead].ToArray());
            await writer.WriteAsync(command, CancellationToken.None);

            // await Task.Delay(300); //emulate real-time speech delay
            bytesRead = await inputStream.ReadAsync(readBuffer);
            size += bytesRead;
        }

        writer.Complete();
        return size;
    }
}
