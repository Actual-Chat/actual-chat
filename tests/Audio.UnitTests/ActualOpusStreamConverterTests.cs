using Stl.IO;

namespace ActualChat.Audio.UnitTests;

public class ActualOpusStreamConverterTests
{
    private ILogger Log { get; }

    public ActualOpusStreamConverterTests(ILogger log)
        => Log = log;

    [Fact]
    public async Task ReadAndWrittenStreamIsTheSame()
    {
        var webMStreamConverter = new WebMStreamConverter(MomentClockSet.Default,  Log);
        var opusStreamConverter = new ActualOpusStreamConverter(MomentClockSet.Default, Log);
        var byteStream = GetAudioFilePath((FilePath)"0000-LONG.webm").ReadByteStream(1024);
        var audio = await webMStreamConverter.FromByteStream(byteStream, default);
        var outByteStreamMemoized = opusStreamConverter.ToByteStream(audio, CancellationToken.None).Memoize();

        var audio1 = await opusStreamConverter.FromByteStream(outByteStreamMemoized.Replay(), CancellationToken.None);
        var outByteStream1 = opusStreamConverter.ToByteStream(audio1, CancellationToken.None);

        var inList = await outByteStreamMemoized.Replay().ToListAsync();
        var outList = await outByteStream1.ToListAsync();
        var inArray = inList.SelectMany(chunk => chunk).ToArray();
        var outArray = outList.SelectMany(chunk => chunk).ToArray();
        inArray.Length.Should().Be(outArray.Length);
        inArray.Should().BeEquivalentTo(outArray);
    }

    [Fact]
    public async Task OneByteSequenceCanBeRead()
    {
        var webMStreamConverter = new WebMStreamConverter(MomentClockSet.Default, Log);
        var opusStreamConverter = new ActualOpusStreamConverter(MomentClockSet.Default, Log);
        var byteStream = GetAudioFilePath((FilePath)"0000-LONG.webm")
            .ReadByteStream(1);
        var audio = await webMStreamConverter.FromByteStream(byteStream, default);
        var outByteStreamMemoized = opusStreamConverter.ToByteStream(audio, CancellationToken.None).Memoize();

        var audio1 = await opusStreamConverter.FromByteStream(outByteStreamMemoized.Replay(), CancellationToken.None);
        var outByteStream1 = opusStreamConverter.ToByteStream(audio1, CancellationToken.None);

        var inList = await outByteStreamMemoized.Replay().ToListAsync();
        var outList = await outByteStream1.ToListAsync();
        var inArray = inList.SelectMany(chunk => chunk).ToArray();
        var outArray = outList.SelectMany(chunk => chunk).ToArray();
        inArray.Length.Should().Be(outArray.Length);
        inArray.Should().BeEquivalentTo(outArray);
    }

    [Fact]
    public async Task SuccessfulReadFromFile()
    {
        var converter = new ActualOpusStreamConverter(MomentClockSet.Default, Log);
        var byteStream = GetAudioFilePath((FilePath)"0000.opuss")
            .ReadByteStream( 1024);
        var audio = await converter.FromByteStream(byteStream, default);
        var outByteStream = converter.ToByteStream(audio, CancellationToken.None);
        var outList = await outByteStream.ToListAsync();
        var outArray = outList.SelectMany(chunk => chunk).ToArray();
        outArray.Length.Should().Be(10571); // we added preSkip and createdAt with this commit
    }

    [Fact(Skip = "Manual")]
    public async Task ReadWriteFile()
    {
        var converter = new ActualOpusStreamConverter(MomentClockSet.Default, Log);
        var byteStream = GetAudioFilePath((FilePath)"silence.opuss")
            .ReadByteStream( 1024);
        await using var outputStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, "data", "silence-prefix.opuss"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite);
        var audio = await converter.FromByteStream(byteStream, default);
        var outByteStream = converter.ToByteStream(audio, CancellationToken.None).Take(101);
        var i = 0;
        await foreach (var x in outByteStream) {
            Log.LogInformation("{I}", i++);
            await outputStream.WriteAsync(x);
        }
        outputStream.Flush();
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
