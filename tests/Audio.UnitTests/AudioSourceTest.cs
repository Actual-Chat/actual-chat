using Stl.IO;

namespace ActualChat.Audio.UnitTests;

public class AudioSourceTest
{
    private readonly ILogger _log;

    public AudioSourceTest(ILogger log)
        => _log = log;

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

    [Fact]
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
        int blobSize = 128 * 1024,
        bool webMStream = true,
        CancellationToken cancellationToken = default)
    {
        var byteStream = GetAudioFilePath(fileName)
            .ReadByteStream(blobSize, cancellationToken);
        var streamAdapter = webMStream
            ? new WebMStreamAdapter(_log)
            : (IAudioStreamAdapter)new ActualOpusStreamAdapter(_log);
        var audio = await streamAdapter.Read(byteStream, cancellationToken);
        var skipped = audio.SkipTo(skipTo, cancellationToken);
        await skipped.WhenFormatAvailable.ConfigureAwait(false);
        return skipped;
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;

    // Private methods

    private Task WriteToFile(AudioSource source, TimeSpan skipTo, FilePath fileName, bool webMStream = true)
    {
        var stream = new FileStream(GetAudioFilePath(fileName), FileMode.OpenOrCreate, FileAccess.ReadWrite);
        var streamAdapter = webMStream
            ? new WebMStreamAdapter(_log)
            : (IAudioStreamAdapter)new ActualOpusStreamAdapter(_log);
        return stream.WriteByteStream(streamAdapter.Write(source, CancellationToken.None),true);
    }
}
