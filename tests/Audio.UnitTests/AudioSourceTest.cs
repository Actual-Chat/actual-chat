using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using Stl.IO;

namespace ActualChat.Audio.UnitTests;

public class AudioSourceTest
{
    private readonly ILogger _logger;

    public AudioSourceTest(ILogger logger)
        => _logger = logger;

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

        offset.Should().Be(TimeSpan.FromMilliseconds(7200));

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
    public async Task SkipWithSmallOffset()
    {
        var audio = await GetAudio("0000-LONG.webm", TimeSpan.FromTicks(334972));
        var offset = TimeSpan.Zero - TimeSpan.FromMilliseconds(1);
        var clusterOffsetMs = 0;
        var blockOffsetMs = -1;
        var state = ValidateWebMSequence(
            new WebMReader(audio.Format.Serialize()),
            ref clusterOffsetMs,
            ref blockOffsetMs);
        await foreach (var frame in audio.GetFrames(default)) {
            frame.Data.Should().NotBeNull();
            frame.Data.Should().NotBeEmpty();
            frame.Offset.Should().BeGreaterThan(offset);
            frame.Offset.Should().BeLessThan(offset.Add(TimeSpan.FromMilliseconds(21)));
            offset = frame.Offset;
            state = ValidateWebMSequence(
                state.IsEmpty
                    ? new WebMReader(frame.Data)
                    : WebMReader.FromState(state).WithNewSource(frame.Data),
                ref clusterOffsetMs,
                ref blockOffsetMs);
        }
    }

    [Fact]
    public async Task SkipWithSmallOffsetReadingSmallChunks()
    {
        var audio = await GetAudio("0000-LONG.webm", TimeSpan.FromTicks(334972), 256);
        var offset = TimeSpan.Zero - TimeSpan.FromMilliseconds(1);
        var clusterOffsetMs = 0;
        var blockOffsetMs = -1;
        var state = ValidateWebMSequence(
            new WebMReader(audio.Format.Serialize()),
            ref clusterOffsetMs,
            ref blockOffsetMs);
        await foreach (var frame in audio.GetFrames(default)) {
            frame.Data.Should().NotBeNull();
            frame.Data.Should().NotBeEmpty();
            frame.Offset.Should().BeGreaterThan(offset);
            frame.Offset.Should().BeLessThan(offset.Add(TimeSpan.FromMilliseconds(21)));
            offset = frame.Offset;
            state = ValidateWebMSequence(
                state.IsEmpty
                    ? new WebMReader(frame.Data)
                    : WebMReader.FromState(state).WithNewSource(frame.Data),
                ref clusterOffsetMs,
                ref blockOffsetMs);
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

    private static WebMReader.State ValidateWebMSequence(
        WebMReader reader,
        ref int clusterOffsetMs,
        ref int blockOffsetMs)
    {
        var lastTimeCode = clusterOffsetMs + blockOffsetMs;
        while (reader.Read()) {
            switch (reader.ReadResultKind) {
                case WebMReadResultKind.BeginCluster:
                    var cluster = (Cluster)reader.ReadResult;
                    clusterOffsetMs = (int)cluster.Timestamp;

                    clusterOffsetMs.Should().BeGreaterThan(lastTimeCode);
                    break;
                case WebMReadResultKind.Block:
                    var block = (Block)reader.ReadResult;
                    blockOffsetMs = block.TimeCode;

                    var newTimeCode = clusterOffsetMs + blockOffsetMs;
                    newTimeCode.Should().BeGreaterThan(lastTimeCode);
                    lastTimeCode = newTimeCode;
                    break;
                case WebMReadResultKind.None:
                case WebMReadResultKind.Ebml:
                case WebMReadResultKind.Segment:
                    break;
                case WebMReadResultKind.CompleteCluster:
                case WebMReadResultKind.BlockGroup:
                default:
                    break;
            }
        }
        lastTimeCode = clusterOffsetMs + blockOffsetMs;
        return reader.GetState();
    }

    private async Task<AudioSource> GetAudio(
        FilePath fileName,
        TimeSpan skipTo = default,
        int blobSize = 128 * 1024,
        int skipBytes = 0,
        CancellationToken cancellationToken = default)
    {
        var blobStream = GetAudioFilePath(fileName)
            .ReadBlobStream(blobSize, cancellationToken)
            .SkipBytes(skipBytes, cancellationToken);
        var audio = new AudioSource(blobStream, new AudioMetadata(), skipTo, _logger, cancellationToken);
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
