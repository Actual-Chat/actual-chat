using Stl.IO;

namespace ActualChat.Audio.UnitTests;

public class OggStreamConverterTest(ILogger log)
{
    private ILogger Log { get; } = log;

    [Fact]
    public async Task WriteStreamTest()
    {
        var webMStreamConverter = new WebMStreamConverter(MomentClockSet.Default, Log);
        var oggOpusStreamConverter = new OggOpusStreamConverter(new OggOpusStreamConverter.Options {
            StreamSerialNumber = 0xDB_26_C1_9B,
            PageDuration = TimeSpan.FromMilliseconds(1000),
        });
        var byteStream = GetAudioFilePath((FilePath)"0000.webm")
            .ReadByteStream(128 * 1024);
        var audio = await webMStreamConverter.FromByteStream(byteStream, default);
        var outByteStream = oggOpusStreamConverter.ToByteStream(audio, CancellationToken.None);
        await using var outputStream = new FileStream(
            Path.Combine(Environment.CurrentDirectory, "data", "0000-out.ogg"),
            FileMode.Truncate,
            FileAccess.ReadWrite);

        await foreach (var chunk in outByteStream)
            await outputStream.WriteAsync(chunk, CancellationToken.None);

        await outputStream.FlushAsync();
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
