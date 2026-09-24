using System.Threading.Channels;
using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public partial class AudioStreamingBackend
{
    private readonly ConcurrentDictionary<StreamId, VoiceOverMix> _speechMixes = new();

    public virtual Task SpeakText(
        ChatId chatId,
        AuthorId authorId,
        string text,
        Language language,
        CancellationToken cancellationToken)
    {
        if (text.IsNullOrEmpty())
            return Task.CompletedTask;

        // The whole message at once: unlike a streamed entry there is nothing more coming, so the
        // transcript is complete from its only diff.
        var streamId = StreamId.New(ThisNode.Ref);
        var diff = new TranscriptDiff(StringDiff.New(text, ""), LinearMapDiff.None) {
            IsStable = true,
            Languages = [language],
        };
        var pushTask = PushTextTranscript(
            streamId, chatId, authorId, RpcStream.New(new[] { diff }.ToAsyncEnumerable()), CancellationToken.None);

        // Detached: posting a message must not wait for anyone to hear it.
        _ = BackgroundTask.Run(
            () => OfferSpeech(chatId, authorId, streamId, pushTask),
            Log,
            $"{nameof(SpeakText)} failed",
            CancellationToken.None);
        return Task.CompletedTask;
    }

    public virtual Task<TimeSpan?> GetSpeechBacklog(StreamId streamId, CancellationToken cancellationToken)
        // Null rather than zero when nothing is speaking this stream: "nobody is listening, write as
        // fast as you like" is a different answer from "the voice is keeping up".
        => Task.FromResult(_speechMixes.TryGetValue(streamId, out var mix) ? mix.SpeechBacklog : (TimeSpan?)null);

    // A posted message is on offer only briefly: long enough for a listening client to notice it
    // and ask, and then for as long as the speaking it started actually runs. Left registered it
    // would keep the chat reporting a speaker who is not speaking.
    private static readonly TimeSpan SpeechPickupWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SpeechPollPeriod = TimeSpan.FromSeconds(0.5);

    private async Task OfferSpeech(ChatId chatId, AuthorId authorId, StreamId streamId, Task pushTask)
    {
        var beginsAt = Clocks.ServerClock.Now;
        var streamInfo = new LiveAudioStreamInfo {
            ChatId = chatId,
            AuthorId = authorId,
            StreamId = streamId.Value,
            BeginsAt = beginsAt,
            SourceBeginsAt = beginsAt,
            Format = AudioSource.DefaultFormat,
            IsTextOnly = false,
            IsSynthesized = true,
            Languages = ApiArray<Language>.Empty,
        };
        await LiveAudioBackend.Register(chatId, streamInfo, CancellationToken.None).ConfigureAwait(false);
        try {
            await WhenSpokenOrIgnored(streamId).ConfigureAwait(false);
        }
        finally {
            await LiveAudioBackend
                .Unregister(chatId, streamId.Value, CancellationToken.None)
                .SilentAwait(false);
            await pushTask.SilentAwait(false);
        }
    }

    private async Task WhenSpokenOrIgnored(StreamId streamId)
    {
        var deadline = Clocks.CpuClock.Now + SpeechPickupWindow;
        while (Clocks.CpuClock.Now < deadline && !_speechMixes.ContainsKey(streamId))
            await Clocks.CpuClock.Delay(SpeechPollPeriod, CancellationToken.None).ConfigureAwait(false);

        // Nobody asked inside the window: nothing was synthesized, and nothing will be.
        var maxDeadline = Clocks.CpuClock.Now + Constants.Audio.MaxStreamDuration;
        while (Clocks.CpuClock.Now < maxDeadline && _speechMixes.ContainsKey(streamId))
            await Clocks.CpuClock.Delay(SpeechPollPeriod, CancellationToken.None).ConfigureAwait(false);
    }

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
            // Registered so a producer can ask how far behind its voice is while it writes
            _speechMixes[speechStreamId] = mix;
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
            // A listener who arrives mid-message starts near the live edge: hearing it from the
            // first word would leave them that far behind the writer for the rest of it, and
            // synthesized speech is never persisted for them to catch up on afterwards. The floor
            // is what keeps that from becoming silence - a short message is spoken in full, and a
            // long one is joined a few seconds back rather than at its last syllable.
            var buffered = sourceMemoizer.ProducedCount;
            var index = 0;
            await foreach (var diff in sourceMemoizer.Replay(cancellationToken).ConfigureAwait(false)) {
                spoken += diff;
                if (++index == buffered && buffered > 0) {
                    var skip = GetSkipLength(spoken.Text, spokenLanguage);
                    if (skip > 0)
                        stabilizer.Next(spoken with { Text = spoken.Text[..skip] });
                }
                if (stabilizer.Next(spoken) is { } chunk)
                    await text.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "RunSpeak: #{StreamId} failed", speechStreamId);
        }
        finally {
            _speechMixes.TryRemove(speechStreamId, out _);
            text.Writer.TryComplete();
            if (synthesizeTask != null)
                await synthesizeTask.SilentAwait(false);
            await mixTask.SilentAwait(false);
        }
    }

    // Replayed rather than folded from the loop below: the language is needed before the first
    // chunk is spoken, and a memoized stream can be enumerated again without consuming it.
    // What to leave unspoken of a message already written when the first listener arrived.
    internal static int GetSkipLength(string text, Language language)
    {
        var floor = SpeechRate.ToCharCount(language, Constants.Audio.MinSpokenTailDuration);
        var skip = text.Length - floor;
        if (skip <= 0)
            return 0; // Short enough to speak in full

        // Start at a word boundary, so the first thing heard is not half a word. A script that
        // does not space its words has none to find, and starts where the floor puts it.
        var boundary = text.IndexOf(' ', skip);
        return boundary < 0 ? skip : boundary + 1;
    }

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
