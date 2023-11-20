using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using ActualChat.IO;
using Stl.IO;

namespace ActualChat.Audio.UnitTests;

public class WebMStreamConverterTest
{
    private ILogger Log { get; }

    public WebMStreamConverterTest(ILogger log)
        => Log = log;

    [Fact]
    public async Task WrittenStreamIsValid()
    {
        var converter = new WebMStreamConverter(MomentClockSet.Default, Log);
        var byteStream = GetAudioFilePath((FilePath)"0000-LONG.webm")
            .ReadByteStream(128 * 1024);
        var audio = await converter.FromByteStream(byteStream, default);
        var outByteStream = converter.ToByteStream(audio, CancellationToken.None);
        var clusterOffsetMs = 0;
        var blockOffsetMs = -1;
        WebMReader.State state = new WebMReader.State();
        await foreach (var chunk in outByteStream) {
            chunk.Should().NotBeNull();
            chunk.Should().NotBeEmpty();
            state = ValidateWebMSequence(
                WebMReader.FromState(state).WithNewSource(chunk),
                ref clusterOffsetMs,
                ref blockOffsetMs);
        }
    }

    [Fact]
    public async Task ReadAndWrittenStreamIsTheSame()
    {
        var converter = new WebMStreamConverter(MomentClockSet.Default, Log) {
            WritingApp = "opus-media-recorder",
            TrackUid = 0x00B6F555106DDDC8,
        };
        var byteStreamMemoized = GetAudioFilePath((FilePath)"0000-LONG.webm")
            .ReadByteStream(128 * 1024)
            .Memoize();
        var audio = await converter.FromByteStream(byteStreamMemoized.Replay(), default);
        var outByteStream = converter.ToByteStream(audio, CancellationToken.None);
        var inList = await byteStreamMemoized.Replay().ToListAsync();
        var outList = await outByteStream.ToListAsync();
        outList[0][31] = 2; // revert doc version to 2 as before
        outList.SelectMany(chunk => chunk).Should().StartWith(inList.SelectMany(chunk => chunk));
    }

    [Fact]
    public async Task OneByteSequenceCanBeRead()
    {
        var converter = new WebMStreamConverter(MomentClockSet.Default, Log);
        var byteStream = GetAudioFilePath((FilePath)"0000-LONG.webm")
            .ReadByteStream(1);
        var audio = await converter.FromByteStream(byteStream, default);
        var outByteStream = converter.ToByteStream(audio, CancellationToken.None);
        var clusterOffsetMs = 0;
        var blockOffsetMs = -1;
        WebMReader.State state = new WebMReader.State();
        await foreach (var chunk in outByteStream) {
            chunk.Should().NotBeNull();
            chunk.Should().NotBeEmpty();
            state = ValidateWebMSequence(
                WebMReader.FromState(state).WithNewSource(chunk),
                ref clusterOffsetMs,
                ref blockOffsetMs);
        }
        // var inList = await byteStream.ToListAsync();
        // var outList = await outByteStream.ToListAsync();
        // inList.Should().BeEquivalentTo(outList);
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;

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

}
