using System.Buffers;
using System.Threading.Channels;
using ActualChat.Blobs;
using Grpc.Core;
using Stl.Time;
using Channel = System.Threading.Channels.Channel;

namespace ActualChat.Audio.UnitTests;


public class AudioSourceProviderTest
{
    [Fact]
    public async Task ExtractMediaSourceFromFile()
    {
        var audioSourceProvider = new AudioSourceProvider();
        var blobChannel = Channel.CreateUnbounded<BlobPart>();
        var audioSourceTask = audioSourceProvider.ExtractMediaSource(blobChannel.Reader, CancellationToken.None);

        _ = ReadFromFile(blobChannel.Writer, "file.webm");

        var audioSource = await audioSourceTask;

        var offset = TimeSpan.Zero;
        await foreach (var audioFrame in audioSource)
            offset = audioFrame.Offset > offset
                ? audioFrame.Offset
                : offset;

        offset.Should().Be(TimeSpan.FromMilliseconds(12120));
    }


    [Fact]
    public async Task SaveMediaSourceToFile()
    {
        var audioSourceProvider = new AudioSourceProvider();
        var blobChannel = Channel.CreateUnbounded<BlobPart>();
        var audioSourceTask = audioSourceProvider.ExtractMediaSource(blobChannel.Reader, CancellationToken.None);

        _ = ReadFromFile(blobChannel.Writer, "file.webm");

        var audioSource = await audioSourceTask;

        await WriteToFile(audioSource, TimeSpan.FromSeconds(5), "result-file.webm");
    }

    [Fact]
    public async Task SkipClusterAndSaveMediaSourceToFile()
    {
        var audioSourceProvider = new AudioSourceProvider();
        var blobChannel = Channel.CreateUnbounded<BlobPart>();
        var audioSourceTask = audioSourceProvider.ExtractMediaSource(blobChannel.Reader, CancellationToken.None);

        _ = ReadFromFile(blobChannel.Writer, "large-file.webm");

        var audioSource = await audioSourceTask;

        await WriteToFile(audioSource, TimeSpan.FromSeconds(40), "result-large-file.webm");
    }


    private static async Task<int> ReadFromFile(ChannelWriter<BlobPart> writer, string fileName)
    {
        var size = 0;
        await using var inputStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, @"data", fileName),
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


    private static async Task WriteToFile(AudioSource source, TimeSpan offset, string fileName)
    {
        await using var fileStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, @"data", fileName),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite);

        var header = Convert.FromBase64String(source.Format.CodecSettings);
        await fileStream.WriteAsync(header);

        await foreach (var audioFrame in source.Frames) {
            if (audioFrame.Offset < offset)
                continue;


            await fileStream.WriteAsync(audioFrame.Data);
        }
    }
}
