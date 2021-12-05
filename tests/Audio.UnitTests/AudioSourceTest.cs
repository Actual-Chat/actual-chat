using Stl.IO;

namespace ActualChat.Audio.UnitTests;

public class AudioSourceTest
{
    [Fact]
    public async Task ExtractFromFile()
    {
        var audio = await GetAudio("file.webm");
        var offset = TimeSpan.Zero;
        await foreach (var frame in audio.GetFrames(default)) {
            frame.Data.Should().NotBeNull();
            frame.Data.Should().NotBeEmpty();
            frame.Offset.Should().BeGreaterOrEqualTo(offset);
            offset = frame.Offset > offset
                ? frame.Offset
                : offset;
        }

        offset.Should().Be(TimeSpan.FromMilliseconds(12240));
    }

    [Fact]
    public async Task ExtractFromFileWithMultipleClusters()
    {
        var audio = await GetAudio("large-file.webm");
        var offset = TimeSpan.Zero;
        await foreach (var frame in audio.GetFrames(default)) {
            frame.Data.Should().NotBeNull();
            frame.Data.Should().NotBeEmpty();
            frame.Offset.Should().BeGreaterOrEqualTo(offset);
            frame.Offset.Should().BeLessThan(offset.Add(TimeSpan.FromMilliseconds(150)));
            offset = frame.Offset > offset
                ? frame.Offset
                : offset;
        }
    }

    [Fact]
    public async Task ExtractFromFileWithOffset()
    {
        var audio = await GetAudio("file.webm", TimeSpan.FromSeconds(5));
        var offset = TimeSpan.Zero;
        await foreach (var frame in audio.GetFrames(default)) {
            frame.Data.Should().NotBeNull();
            frame.Data.Should().NotBeEmpty();
            frame.Offset.Should().BeGreaterOrEqualTo(offset);
            offset = frame.Offset > offset
                ? frame.Offset
                : offset;
        }

        offset.Should().Be(TimeSpan.FromMilliseconds(7240));

        await WriteToFile(audio, default, "file-with-offset.webm");
    }

    [Fact(Skip = "It was caused by something else / wrong assumptions")]
    public async Task ExtractFromFileWithWrongByteOffset()
    {
        var audio = await GetAudio("file.webm", TimeSpan.Zero, 1);
        var offset = TimeSpan.Zero;
        await foreach (var frame in audio.GetFrames(default)) {
            frame.Data.Should().NotBeNull();
            frame.Data.Should().NotBeEmpty();
            frame.Offset.Should().BeGreaterOrEqualTo(offset);
            offset = frame.Offset > offset
                ? frame.Offset
                : offset;
        }

        offset.Should().Be(TimeSpan.FromMilliseconds(12240));
    }

    [Fact(Skip = "Not fixed yet")]
    public async Task ExtractFromLargeFileWithOffset()
    {
        var audio = await GetAudio("0002.webm", TimeSpan.FromSeconds(45));
        var offset = TimeSpan.Zero;
        await foreach (var frame in audio.GetFrames(default)) {
            frame.Data.Should().NotBeNull();
            frame.Data.Should().NotBeEmpty();
            frame.Offset.Should().BeGreaterOrEqualTo(offset);
            frame.Offset.Should().BeLessThan(offset.Add(TimeSpan.FromMilliseconds(150)));
            offset = frame.Offset > offset
                ? frame.Offset
                : offset;
        }
    }

    [Fact]
    public async Task SaveToFile()
    {
        var audio = await GetAudio("file.webm");
        await WriteToFile(audio, TimeSpan.FromSeconds(5), "result-file.webm");
    }


    [Fact]
    public async Task SkipClusterAndSaveToFile()
    {
        var audio = await GetAudio("large-file.webm");
        await WriteToFile(audio, TimeSpan.FromSeconds(40), "result-large-file.webm");
    }

    private async Task<AudioSource> GetAudio(
        FilePath fileName,
        TimeSpan skipTo = default,
        int skipBytes = 0,
        CancellationToken cancellationToken = default)
    {
        var blobStream = GetAudioFilePath(fileName)
            .ReadBlobStream(cancellationToken)
            .SkipBytes(skipBytes, cancellationToken);
        var audio = new AudioSource(blobStream, skipTo, null, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;

    // Private methods

    private static Task WriteToFile(AudioSource source, TimeSpan skipTo, FilePath fileName)
    {
        var stream = new FileStream(GetAudioFilePath(fileName), FileMode.OpenOrCreate, FileAccess.ReadWrite);
        return stream.WriteBlobStream(source.GetBlobStream(default), true);
    }
}

public static class BlobStreamExt
{
    public static async IAsyncEnumerable<BlobPart> SkipBytes(
        this IAsyncEnumerable<BlobPart> that,
        int bytes,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        await foreach (var blobPart in that.WithCancellation(cancellationToken))
            if (bytes >= blobPart.Data.Length)
                bytes -= blobPart.Data.Length;
            else {
                yield return blobPart with { Data = blobPart.Data[bytes..] };

                bytes = 0;
            }
    }
}
