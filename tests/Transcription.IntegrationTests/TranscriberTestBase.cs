using ActualChat.Audio;
using ActualLab.IO;

namespace ActualChat.Transcription.IntegrationTests;

public abstract class TranscriberTestBase(ITestOutputHelper @out, ILogger? log = null)
    : TestBase(@out, log)
{
    private const string MissingFileId = "00000000-0000-0000-0000-000000000000";

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

    // Soniox caps stored files and stored transcriptions per organization, and both caps are shared
    // by every environment and key we own - the GET endpoints are project-scoped, so what fills them
    // isn't even visible from here, let alone deletable. A capped account is an outage, so this
    // throws rather than quietly passing a test that never ran.
    protected async Task RequireSonioxCapacity(SonioxClient client, FilePath audioFileName)
    {
        // SonioxOfflineTranscriber turns every failure into null so its failover can try the next
        // provider, which leaves the transcriber tests unable to tell a capped account from a broken
        // one - hence asking the two caps here, where the answers still carry Soniox's own message.
        string fileId;
        var stream = File.OpenRead(GetAudioFilePath(audioFileName));
        await using (stream.ConfigureAwait(false))
            fileId = await client.UploadFile(stream, "probe.opus", CancellationToken.None);
        await client.DeleteFile(fileId, CancellationToken.None);

        // The create rejects an over-cap organization before it looks at the file id, so a
        // nonexistent one asks about the transcription cap without creating anything. Every other
        // rejection is that file id doing its job.
        var request = new Dictionary<string, object?> {
            ["file_id"] = MissingFileId,
            ["model"] = SonioxOfflineTranscriber.Model,
        };
        try {
            var transcriptionId = await client.CreateTranscription(request, CancellationToken.None);
            await client.DeleteTranscription(transcriptionId, CancellationToken.None);
        }
        catch (Exception e) when (!e.Message.Contains("limit_exceeded")) {
            WriteLine($"Transcription cap probe rejected as expected: {e.Message}");
        }
    }
}
