using ActualChat.Audio;
using ActualChat.IO;
using ActualLab.IO;

namespace ActualChat.Streaming.UnitTests;

public class AudioSourceTest(ILogger log)
{
    private ILogger Log { get; } = log;

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

    [Fact(Skip = "Run manually to convert .opuss file")]
    public async Task ConvertFiles()
    {
        var audio = await GetAudio("0003.opuss");
        await WriteToFile(audio, TimeSpan.Zero, "0003.webm");
    }

    private async Task<AudioSource> GetAudio(
        FilePath fileName,
        TimeSpan skipTo = default,
        int blobSize = 128 * 1024,
        bool? isWebMStream = null,
        CancellationToken cancellationToken = default)
    {
        var isWebMStream1 = isWebMStream ?? string.Equals(fileName.Extension, "webm", StringComparison.InvariantCultureIgnoreCase);
        var byteStream = GetAudioFilePath(fileName)
            .ReadByteStream(blobSize, cancellationToken);
        var converter = isWebMStream1
            ? new WebMStreamConverter(MomentClockSet.Default, Log)
            : (IAudioStreamConverter)new ActualOpusStreamConverter(MomentClockSet.Default, Log);
        var audio = await converter.FromByteStream(byteStream, cancellationToken);
        var skipped = audio.SkipTo(skipTo, cancellationToken);
        return skipped;
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;

    // Private methods

    private async Task WriteToFile(AudioSource source, TimeSpan skipTo, FilePath fileName, bool? isWebMStream = null)
    {
        var isWebMStream1 = isWebMStream ?? string.Equals(fileName.Extension, ".webm", StringComparison.InvariantCultureIgnoreCase);
        await using var stream = new FileStream(GetAudioFilePath(fileName), FileMode.OpenOrCreate, FileAccess.ReadWrite);
        var converter = isWebMStream1
            ? new WebMStreamConverter(MomentClockSet.Default, Log)
            : (IAudioStreamConverter)new ActualOpusStreamConverter(MomentClockSet.Default, Log);
        await stream.WriteByteStream(converter.ToByteStream(source.SkipTo(skipTo, CancellationToken.None), CancellationToken.None),true);
    }
}
