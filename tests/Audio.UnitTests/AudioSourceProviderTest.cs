using System.Buffers;
using ActualChat.Blobs;

namespace ActualChat.Audio.UnitTests;

public class AudioSourceProviderTest
{
    [Fact]
    public async Task ExtractMediaSourceFromFile()
    {
        var audioSourceProvider = new AudioSourceProvider();
        var blobChannel = Channel.CreateUnbounded<BlobPart>();
        var audioSourceTask =
            audioSourceProvider.CreateMediaSource(
                blobChannel.Reader.ReadAllAsync(), default,
                CancellationToken.None);

        _ = ReadFromFile(blobChannel.Writer, "file.webm");

        var audioSource = await audioSourceTask;

        var offset = TimeSpan.Zero;
        await foreach (var audioFrame in audioSource) {
            audioFrame.Data.Should().NotBeNull();
            audioFrame.Data.Should().NotBeEmpty();
            audioFrame.Offset.Should().BeGreaterOrEqualTo(offset);
            offset = audioFrame.Offset > offset
                ? audioFrame.Offset
                : offset;
        }

        offset.Should().Be(TimeSpan.FromMilliseconds(12240));
    }

    [Fact]
    public async Task ExtractMediaSourceFromFileWithMultipleCluster()
    {
        var audioSourceProvider = new AudioSourceProvider();
        var blobChannel = Channel.CreateUnbounded<BlobPart>();
        var audioSourceTask = audioSourceProvider
            .CreateMediaSource(
                blobChannel.Reader.ReadAllAsync(), default,
                CancellationToken.None);

        _ = ReadFromFile(blobChannel.Writer, "large-file.webm");

        var audioSource = await audioSourceTask;

        var offset = TimeSpan.Zero;
        await foreach (var audioFrame in audioSource) {
            audioFrame.Data.Should().NotBeNull();
            audioFrame.Data.Should().NotBeEmpty();
            audioFrame.Offset.Should().BeGreaterOrEqualTo(offset);
            audioFrame.Offset.Should().BeLessThan(offset.Add(TimeSpan.FromMilliseconds(150)));
            offset = audioFrame.Offset > offset
                ? audioFrame.Offset
                : offset;
        }
    }

    [Fact]
    public async Task ExtractMediaSourceFromFileWithOffset()
    {
        var audioSourceProvider = new AudioSourceProvider();
        var blobChannel = Channel.CreateUnbounded<BlobPart>();
        var audioSourceTask =
            audioSourceProvider.CreateMediaSource(
                blobChannel.Reader.ReadAllAsync(), TimeSpan.FromSeconds(5),
                CancellationToken.None);

        _ = ReadFromFile(blobChannel.Writer, "file.webm");

        var audioSource = await audioSourceTask;

        var offset = TimeSpan.Zero;
        await foreach (var audioFrame in audioSource) {
            audioFrame.Data.Should().NotBeNull();
            audioFrame.Data.Should().NotBeEmpty();
            audioFrame.Offset.Should().BeGreaterOrEqualTo(offset);
            offset = audioFrame.Offset > offset
                ? audioFrame.Offset
                : offset;
        }

        offset.Should().Be(TimeSpan.FromMilliseconds(7240));

        await WriteToFile(audioSource, default, "file-with-offset.webm");
    }

    [Fact(Skip = "Not fixed yet")]
    public async Task ExtractMediaSourceFromLargeFileWithOffset()
    {
        var audioSourceProvider = new AudioSourceProvider();
        var blobChannel = Channel.CreateUnbounded<BlobPart>();
        var audioSourceTask = audioSourceProvider
            .CreateMediaSource(
                blobChannel.Reader.ReadAllAsync(), TimeSpan.FromSeconds(45),
                CancellationToken.None);

        _ = ReadFromFile(blobChannel.Writer, "0002.webm");

        var audioSource = await audioSourceTask;

        var offset = TimeSpan.Zero;
        await foreach (var audioFrame in audioSource) {
            audioFrame.Data.Should().NotBeNull();
            audioFrame.Data.Should().NotBeEmpty();
            audioFrame.Offset.Should().BeGreaterOrEqualTo(offset);
            audioFrame.Offset.Should().BeLessThan(offset.Add(TimeSpan.FromMilliseconds(150)));
            offset = audioFrame.Offset > offset
                ? audioFrame.Offset
                : offset;
        }
    }


    [Fact]
    public async Task SaveMediaSourceToFile()
    {
        var audioSourceProvider = new AudioSourceProvider();
        var blobChannel = Channel.CreateUnbounded<BlobPart>();
        var audioSourceTask =
            audioSourceProvider.CreateMediaSource(
                blobChannel.Reader.ReadAllAsync(), default,
                CancellationToken.None);

        _ = ReadFromFile(blobChannel.Writer, "file.webm");

        var audioSource = await audioSourceTask;

        await WriteToFile(audioSource, TimeSpan.FromSeconds(5), "result-file.webm");
    }


    [Fact]
    public async Task SkipClusterAndSaveMediaSourceToFile()
    {
        var audioSourceProvider = new AudioSourceProvider();
        var blobChannel = Channel.CreateUnbounded<BlobPart>();
        var audioSourceTask =
            audioSourceProvider.CreateMediaSource(
                blobChannel.Reader.ReadAllAsync(), default,
                CancellationToken.None);

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
        while (bytesRead < 1 * 1024)
            bytesRead += await inputStream.ReadAsync(readBuffer[bytesRead..]);
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


    private static async Task WriteToFile(AudioSource source, TimeSpan skipTo, string fileName)
    {
        await using var fileStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, @"data", fileName),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite);

        var header = Convert.FromBase64String(source.Format.CodecSettings);
        await fileStream.WriteAsync(header);

        await foreach (var audioFrame in source.Frames) {
            if (audioFrame.Offset < skipTo)
                continue;

            await fileStream.WriteAsync(audioFrame.Data);
        }
    }
}
