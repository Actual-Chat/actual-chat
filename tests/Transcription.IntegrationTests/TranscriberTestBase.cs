using ActualChat.Audio;
using ActualLab.IO;

namespace ActualChat.Transcription.IntegrationTests;

public abstract class TranscriberTestBase(ITestOutputHelper @out, ILogger? log = null)
    : TestBase(@out, log)
{
    protected async Task<AudioSource> GetAudio(FilePath fileName, bool? webMStream = null, bool withDelay = false)
    {
        var byteStream = GetAudioFilePath(fileName).ReadByteStream(1024, CancellationToken.None);
        var isWebMStream = webMStream ?? fileName.Extension == ".webm";
        var converter = isWebMStream
            ? (IAudioStreamConverter)new WebMStreamConverter(MomentClockSet.Default, Log)
            : new ActualOpusStreamConverter(MomentClockSet.Default, Log);
        var audio = await converter.FromByteStream(byteStream, CancellationToken.None);
        if (!withDelay)
            return audio;

        var delayedFrames = audio.GetFrames(CancellationToken.None)
            .Select(async (AudioFrame f, CancellationToken _) => {
                await Task.Delay(20).ConfigureAwait(false);
                return f;
            });
        return new AudioSource(
            MomentClockSet.Default.SystemClock.Now,
            audio.Format,
            delayedFrames,
            TimeSpan.Zero,
            Log,
            CancellationToken.None);
    }

    protected static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
