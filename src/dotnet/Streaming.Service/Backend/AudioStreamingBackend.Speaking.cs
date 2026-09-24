using System.Threading.Channels;
using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

public partial class AudioStreamingBackend
{
    // Speaking a text entry is the mirror image of recording one: recording turns audio into text,
    // this turns text into audio. It shares RunDub's publishing but none of its translation
    // machinery - there is nothing to decide, translate, stabilize or skip a backlog of.
    private async Task RunSpeak(
        StreamId speechStreamId,
        TaskCompletionSource whenPublishedSource,
        CancellationToken cancellationToken)
    {
        var sourceStreamId = speechStreamId.BaseStreamId;
        VoiceOverMix mix;
        Task mixTask;
        AsyncMemoizer<AudioFrame> memoizer;
        try {
            // No original: a text entry has no audio, so the mix is the synthesis alone
            mix = new VoiceOverMix(null, new DubActivity(), Clocks, Log);
            mixTask = PublishMix(speechStreamId, mix, out memoizer, cancellationToken);
            await mix.WhenCaughtUp.WaitAsync(cancellationToken).ConfigureAwait(false);
            whenPublishedSource.TrySetResult();
        }
        catch (Exception e) {
            whenPublishedSource.TrySetException(e);
            if (!e.IsCancellationOf(cancellationToken))
                Log.LogError(e, "RunSpeak: #{StreamId} failed to publish", speechStreamId);
            return;
        }

        var text = Channel.CreateUnbounded<string>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
        });
        Task? synthesizeTask = null;
        try {
            var sourceMemoizer = await WaitForSourceTranscript(sourceStreamId, null, cancellationToken)
                .ConfigureAwait(false);
            if (sourceMemoizer == null) {
                Log.LogWarning("RunSpeak: #{StreamId} - no transcript to speak", speechStreamId);
                return;
            }

            // A dub reads its voice's language off the stream id's suffix; speech is served on the
            // text stream's own id, which has none - so it comes from what the producer declared.
            var language = await GetSpokenLanguage(sourceMemoizer, cancellationToken).ConfigureAwait(false);
            if (language is not { } spokenLanguage) {
                Log.LogWarning("RunSpeak: #{StreamId} - the transcript names no language to speak it in",
                    speechStreamId);
                return;
            }

            synthesizeTask = StartSynthesis(
                speechStreamId, spokenLanguage, text.Reader, mix, mixTask, null, cancellationToken);

            // DubStabilizer chunks at boundaries a synthesizer can speak well; the source transcript
            // is fed to it directly, where a dub would feed the translation of it
            var stabilizer = new DubStabilizer();
            var spoken = Transcript.Empty;
            await foreach (var diff in sourceMemoizer.Replay(cancellationToken).ConfigureAwait(false)) {
                spoken += diff;
                if (stabilizer.Next(spoken) is { } chunk)
                    await text.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "RunSpeak: #{StreamId} failed", speechStreamId);
        }
        finally {
            text.Writer.TryComplete();
            if (synthesizeTask != null)
                await synthesizeTask.SilentAwait(false);
            await mixTask.SilentAwait(false);
        }
    }

    // Replayed rather than folded from the loop below: the language is needed before the first
    // chunk is spoken, and a memoized stream can be enumerated again without consuming it.
    private static async Task<Language?> GetSpokenLanguage(
        AsyncMemoizer<TranscriptDiff> sourceMemoizer,
        CancellationToken cancellationToken)
    {
        var transcript = Transcript.Empty;
        await foreach (var diff in sourceMemoizer.Replay(cancellationToken).ConfigureAwait(false)) {
            transcript += diff;
            if (transcript.Languages.Length > 0)
                return transcript.Languages[0];
        }
        return null;
    }
}
