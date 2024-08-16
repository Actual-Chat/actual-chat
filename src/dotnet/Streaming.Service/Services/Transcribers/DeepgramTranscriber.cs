using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.RegularExpressions;
using ActualChat.Audio;
using ActualChat.Streaming.Module;
using ActualChat.Transcription;
using Deepgram;
using Deepgram.Constants;
using Deepgram.Models.Authenticate.v1;
using Deepgram.Models.Listen.v1.WebSocket;
using static ActualChat.Constants.Transcription.Deepgram;

namespace ActualChat.Streaming.Services.Transcribers;

#pragma warning disable CA1826

public partial class DeepgramTranscriber : ITranscriber
{
    [GeneratedRegex(@"([\?\!\.]\s*$)|(^\s*$)", RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex CompleteSentenceOrEmptyRegexFactory();

    [GeneratedRegex(@"(\s+$)|(^\s*$)", RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex EndsWithWhitespaceOrEmptyRegexFactory();

    private static readonly Regex CompleteSentenceOrEmptyRegex = CompleteSentenceOrEmptyRegexFactory();
    private static readonly Regex EndsWithWhitespaceOrEmptyRegex = EndsWithWhitespaceOrEmptyRegexFactory();

    private ILogger Log { get; }
    private MomentClockSet Clocks { get; }
    private StreamingSettings StreamingSettings { get; }
    private OggOpusStreamConverter OggOpusStreamConverter { get; }

    public DeepgramTranscriber(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
        StreamingSettings = services.GetRequiredService<StreamingSettings>();
        OggOpusStreamConverter = new OggOpusStreamConverter(new OggOpusStreamConverter.Options {
            PageDuration = TimeSpan.FromMilliseconds(200),
        });
    }

    public async Task Transcribe(
        string audioStreamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        ChannelWriter<Transcript> output,
        CancellationToken cancellationToken = default)
    {
        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var deepgramClient = new ListenWebSocketClient(
            StreamingSettings.DeepgramKey,
            new DeepgramWsClientOptions {
                KeepAlive = true,
            });

        var transcriptState = new DeepgramTranscribeState(audioSource, output);
        var whenCompletedSource = TaskCompletionSourceExt.New();
        var whenConnectedSource = TaskCompletionSourceExt.New();
        var whenCompleted = whenCompletedSource.Task;
        var whenConnected = whenConnectedSource.Task;


        Exception? error = null;
        try {
            deepgramClient.Subscribe(HandleConnectionOpened);
            deepgramClient.Subscribe(HandleConnectionClosed);
            deepgramClient.Subscribe(HandleConnectionError);
            deepgramClient.Subscribe(HandleTranscriptReceived);

            var language = GetSupportedLanguage(options);
            var liveSchema = new LiveSchema {
                Language = language,
                Punctuate = true,
                Diarize = false,
                Encoding = AudioEncoding.OggOpus,
                Channels = 1,
                InterimResults = true,
                Model = "nova-2",
            };
            await deepgramClient.Connect(liveSchema, tokenSource)
                .ConfigureAwait(false);

            await whenConnected.ConfigureAwait(false);
            await PushAudio(transcriptState, deepgramClient, cancellationToken).ConfigureAwait(false);

            await whenCompleted.ConfigureAwait(false);
            await deepgramClient.Stop(tokenSource).ConfigureAwait(false);
        }
        catch (Exception e) {
            error = e;
            Log.LogError(e, "Error transcribing {StreamId}", audioStreamId);
            throw;
        }
        finally {
            output.TryComplete(error);
        }
        return;

        string GetSupportedLanguage(TranscriptionOptions options1)
        {
            return options1.Language.Id.Value switch {
                "en-US" => "en-US",
                "en-GB" => "en-GB",
                "en-IN" => "en-IN",
                "fr-FR" => "fr",
                "fr-CA" => "fr-CA",
                "de-DE" => "de",
                "hi-IN" => "hi",
                "pt-BR" => "pt-BR",
                "pt-PT" => "pt",
                "es-ES" => "es",
                "es-MX" => "es-419",
                "es-US" => "es-419",
                "ru-RU" => "ru",
                "zh-CN" => "zh-CN",
                _ => throw StandardError.NotSupported(typeof(DeepgramTranscriber), $"Language '{options1.Language.Id}' is not supported"),
            };
        }

        [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
        void HandleConnectionOpened(object? sender, OpenResponse e)
            => whenConnectedSource.SetResult();

        void HandleTranscriptReceived(object? sender, ResultResponse e)
            => ProcessResponse(transcriptState, e);

        void HandleConnectionClosed(object? sender, CloseResponse e)
            => whenCompletedSource.TrySetResult();

        void HandleConnectionError(object? sender, ErrorResponse e)
            => whenCompletedSource.TrySetException(new TranscriptionException(e.Message, e.Description));
    }

    private async Task PushAudio(
        DeepgramTranscribeState state,
        ListenWebSocketClient deepgramClient,
        CancellationToken cancellationToken)
    {
        var audioSource = state.AudioSource;
        try {
            var byteFrameStream = OggOpusStreamConverter.ToByteFrameStream(audioSource, cancellationToken);
            var clock = Clocks.CpuClock;
            var startedAt = clock.Now;
            var nextChunkAt = startedAt;
            await foreach (var (chunk, lastFrame) in byteFrameStream.ConfigureAwait(false)) {
                var delay = nextChunkAt - clock.Now;
                if (delay > TimeSpan.Zero)
                    await clock.Delay(delay, cancellationToken).ConfigureAwait(false);

                deepgramClient.SendBinary(chunk);

                if (lastFrame != null) {
                    var processedAudioDuration = (lastFrame.Offset + lastFrame.Duration).Positive();
                    if (audioSource.WhenDurationAvailable.IsCompletedSuccessfully())
                        processedAudioDuration = TimeSpanExt.Min(audioSource.Duration, processedAudioDuration);
                    // state.ProcessedAudioDuration = (float)processedAudioDuration.TotalSeconds;
                    nextChunkAt = startedAt
                        + TimeSpan.FromSeconds(processedAudioDuration.TotalSeconds / Speed)
                        - TimeSpan.FromMilliseconds(50);
                }
            }
        }
        catch (Exception e) {
            Log.LogError(e, $"{nameof(PushAudio)} failed");
            throw;
        }
        finally {
            deepgramClient.SendFinalize();
        }
    }

    private static void ProcessResponse(DeepgramTranscribeState state, ResultResponse result)
    {
        var isFinal = result.IsFinal ?? false;
        var suffix = result.Channel?.Alternatives?.FirstOrDefault()?.Transcript ?? "";
        var endTime = (float?)result.Duration ?? 0;
        var appendToUnstable = state.IsLastAppendStable;
        if (isFinal) {
            if (TryParseFinal(state, result, out suffix, out var map))
                state.Append(suffix, map).MakeStable();
            else
                state.MakeStable();
        }
        else {
            suffix = FixSuffix(state[appendToUnstable].Text, suffix);
            state.Append(suffix, endTime, appendToUnstable);
        }

        if (state.Unstable.Length != 0)
            _ = state.Output.WriteAsync(state.Unstable);
    }

    private static bool TryParseFinal(
        DeepgramTranscribeState state,
        ResultResponse result,
        out string text,
        out LinearMap timeMap)
    {
        var lastStable = state.Stable;
        var lastStableTextLength = lastStable.Text.Length;
        var lastStableDuration = lastStable.TimeMap.YRange.End;

        var alternative = result.Channel?.Alternatives?.FirstOrDefault();
        var endTime = ((float?)result.Start + (float?)result.Duration) ?? 0;
        if (alternative == null || alternative.Transcript.IsNullOrEmpty()) {
            text = "";
            return false;
        }

        text = alternative.Transcript;
        if (lastStableTextLength > 0 && text.Length > 0 && !char.IsWhiteSpace(text[0]))
            text = " " + text;

        var mapPoints = new List<Vector2>();
        var parsedOffset = 0;
        var parsedDuration = lastStableDuration;
        foreach (var word in alternative.Words ?? []) {
            var wordStartTime = (float?)word.Start ?? 0;
            if (wordStartTime < parsedDuration)
                continue;

            if (word.PunctuatedWord == null)
                continue;

            var wordStart = text.OrdinalIgnoreCaseIndexOf(word.PunctuatedWord, parsedOffset);
            if (wordStart < 0)
                continue;

            var wordEndTime = (float?)word.End ?? 0;
            var wordEnd = wordStart + word.PunctuatedWord.Length;

            mapPoints.Add(new Vector2(lastStableTextLength + wordStart, wordStartTime));
            mapPoints.Add(new Vector2(lastStableTextLength + wordEnd, wordEndTime));

            parsedDuration = wordStartTime;
            parsedOffset = wordStart + word.PunctuatedWord.Length;
        }

        if (mapPoints.Count == 0) {
            timeMap = default;
            return false;
        }

        var lastPoint = mapPoints[^1];
        var veryLastPoint = new Vector2(lastStableTextLength + text.Length, endTime);
        if (Math.Abs(lastPoint.X - veryLastPoint.X) < 0.1)
            mapPoints[^1] = veryLastPoint;
        else
            mapPoints.Add(veryLastPoint);
        timeMap = new LinearMap(mapPoints.ToArray()).Simplify(Transcript.TimeMapEpsilon);
        return true;
    }

    private static string FixSuffix(string prefix, string suffix)
    {
        var firstLetterIndex = Transcript.ContentStartRegex.Match(suffix).Length;
        if (firstLetterIndex == suffix.Length)
            return suffix; // Suffix is all whitespace or empty

        if (firstLetterIndex == 0 && !EndsWithWhitespaceOrEmptyRegex.IsMatch(prefix)) {
            // Add spacer
            suffix = " " + suffix;
            firstLetterIndex++;
        }
        else if (firstLetterIndex > 0 && EndsWithWhitespaceOrEmptyRegex.IsMatch(prefix)) {
            // Remove spacer
            suffix = suffix[firstLetterIndex..];
            firstLetterIndex = 0;
        }

        if (CompleteSentenceOrEmptyRegex.IsMatch(prefix))
            suffix = suffix.Capitalize(firstLetterIndex);

        return suffix;
    }
}
