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

    // Soniox caps stored files per organization, and that cap is shared by every environment and
    // key we own - GET /v1/files is project-scoped, so what fills it isn't even visible from here,
    // let alone deletable. Treated like an unset key: an environment we can't fix from the test.
    protected bool IsExternalQuotaExceeded(Exception e)
    {
        if (!e.Message.Contains("limit_exceeded"))
            return false;

        WriteLine($"External quota exceeded - skipping. {e.Message}");
        return true;
    }

    // SonioxOfflineTranscriber turns every failure into null so its failover can try the next
    // provider, which leaves a test unable to tell a full account from a broken one. An upload is
    // the only way to ask; it's deleted right back on the way out.
    protected async Task<bool> IsSonioxAtCapacity(SonioxClient client, FilePath audioFileName)
    {
        string fileId;
        var stream = File.OpenRead(GetAudioFilePath(audioFileName));
        await using (stream.ConfigureAwait(false)) {
            try {
                fileId = await client.UploadFile(stream, "probe.opus", CancellationToken.None);
            }
            catch (Exception e) when (IsExternalQuotaExceeded(e)) {
                return true;
            }
        }
        await client.DeleteFile(fileId, CancellationToken.None);
        return false;
    }
}
