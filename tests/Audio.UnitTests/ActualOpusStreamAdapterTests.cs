using Stl.IO;

namespace ActualChat.Audio.UnitTests;

public class ActualOpusStreamAdapterTests
{
    private readonly ILogger _logger;

    public ActualOpusStreamAdapterTests(ILogger logger)
        => _logger = logger;

    [Fact]
    public async Task ReadAndWrittenStreamIsTheSame()
    {
        var webMStreamAdapter = new WebMStreamAdapter(_logger);
        var streamAdapter = new ActualOpusStreamAdapter(_logger);
        var byteStream = GetAudioFilePath((FilePath)"0000-LONG.webm")
            .ReadByteStream(1024);
        var audio = await webMStreamAdapter.Read(byteStream, default);
        var outByteStreamMemoized = streamAdapter.Write(audio, CancellationToken.None).Memoize();

        var audio1 = await streamAdapter.Read(outByteStreamMemoized.Replay(), CancellationToken.None);
        var outByteStream1 = streamAdapter.Write(audio1, CancellationToken.None);

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
        var webMStreamAdapter = new WebMStreamAdapter(_logger);
        var streamAdapter = new ActualOpusStreamAdapter(_logger);
        var byteStream = GetAudioFilePath((FilePath)"0000-LONG.webm")
            .ReadByteStream(1);
        var audio = await webMStreamAdapter.Read(byteStream, default);
        var outByteStreamMemoized = streamAdapter.Write(audio, CancellationToken.None).Memoize();

        var audio1 = await streamAdapter.Read(outByteStreamMemoized.Replay(), CancellationToken.None);
        var outByteStream1 = streamAdapter.Write(audio1, CancellationToken.None);

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
        var streamAdapter = new ActualOpusStreamAdapter(_logger);
        var byteStream = GetAudioFilePath((FilePath)"0000.opuss")
            .ReadByteStream( 1024);
        var audio = await streamAdapter.Read(byteStream, default);
        var outByteStream = streamAdapter.Write(audio, CancellationToken.None);
        var outList = await outByteStream.ToListAsync();
        var outArray = outList.SelectMany(chunk => chunk).ToArray();
        outArray.Length.Should().Be(10561);
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
