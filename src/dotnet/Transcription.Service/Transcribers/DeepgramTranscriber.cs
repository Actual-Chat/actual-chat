using System.Numerics;
using System.Text.RegularExpressions;
using ActualChat.Audio;
using ActualChat.Module;
using ActualLab.Diagnostics;
using Deepgram;
using Deepgram.Constants;
using Deepgram.Models.Authenticate.v1;
using Deepgram.Models.Listen.v2.WebSocket;
using static ActualChat.Constants.Transcription.Deepgram;

namespace ActualChat.Transcription;

#pragma warning disable CA1826
public sealed partial class DeepgramTranscriber : ITranscriber
{
    [GeneratedRegex(@"([\?\!\.]\s*$)|(^\s*$)", RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex CompleteSentenceOrEmptyRegexFactory();

    [GeneratedRegex(@"(\s+$)|(^\s*$)", RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex EndsWithWhitespaceOrEmptyRegexFactory();

    private static readonly Regex CompleteSentenceOrEmptyRegex = CompleteSentenceOrEmptyRegexFactory();
    private static readonly Regex EndsWithWhitespaceOrEmptyRegex = EndsWithWhitespaceOrEmptyRegexFactory();
    private static readonly HashSet<Language> Nova3Languages = new [] {
            Languages.Danish,
            Languages.Dutch,
            Languages.French,
            Languages.FrenchCA,
            Languages.German,
            Languages.Portuguese,
            Languages.PortugueseBR,
            Languages.Spanish,
            Languages.SpanishMX,
            Languages.SpanishUS,
            Languages.Czech,
            Languages.Russian,
            Languages.Polish,
            Languages.Ukrainian,
            Languages.Finnish,
            Languages.Hindi,
            Languages.Japanese,
            Languages.Korean,
            Languages.Vietnamese,
            Languages.Italian,
            Languages.Turkish,
            Languages.Indonesian,
        }
        .ToHashSet();
    private static bool DebugMode => Constants.DebugMode.TranscriberDeepgram;
    private IServiceProvider Services { get; }
    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;
    private MomentClockSet Clocks { get; }
    private CoreServerSettings CoreServerSettings { get; }
    private OggOpusStreamConverter OggOpusStreamConverter { get; }
    public TranscriberInfo Info { get; } = new() {
        Id = TranscriberId.DeepgramStream,
        DriverId = "deepgram",
        Kind = TranscriberKind.StreamOnly,
        Languages = DeepgramLanguage.Supported,
        IsLanguageDetectionSupported = true,
    };

    public DeepgramTranscriber(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
        CoreServerSettings = services.GetRequiredService<CoreServerSettings>();
        OggOpusStreamConverter = new OggOpusStreamConverter(new OggOpusStreamConverter.Options {
            PageDuration = Constants.Transcription.StreamPageDuration,
        });
    }

    public async Task Transcribe(
        string audioStreamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        ChannelWriter<Transcript> output,
        CancellationToken cancellationToken = default)
    {
        var transcriptState = new DeepgramTranscribeState(audioSource, output);
        var whenCompletedSource = AsyncTaskMethodBuilderExt.New();
        Exception? error = null;
        try {
            using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var apiKey = CoreServerSettings.DeepgramKey;
            using var deepgramClient = new ListenWebSocketClient(
                apiKey,
                new DeepgramWsClientOptions(apiKey, keepAlive: true));

            var whenCompleted = whenCompletedSource.Task;

            await deepgramClient.Subscribe(HandleConnectionClosed).ConfigureAwait(false);
            await deepgramClient.Subscribe(HandleConnectionError).ConfigureAwait(false);
            await deepgramClient.Subscribe(HandleTranscriptReceived).ConfigureAwait(false);

            var language = options.Language;
            var schemaLanguage = language.ToDeepgram();
            var isNova3Usable = true;
            if (!options.DetectLanguage)
                isNova3Usable = IsNova3Language(language);
            else {
                schemaLanguage = "multi";
                foreach (var languageCandidate in options.LanguageCandidates)
                    if (!IsNova3Language(languageCandidate)) {
                        isNova3Usable = false;
                        break;
                    }
            }
            var model = isNova3Usable ? "nova-3" : "nova-2";
            var liveSchema = new LiveSchema {
                Language = schemaLanguage,
                Punctuate = true,
                Diarize = false,
                Encoding = AudioEncoding.OggOpus,
                Channels = 1,
                EndPointing = "100",
                SmartFormat = true,
                InterimResults = true,
                Model = model,
            };
            var isConnected = await deepgramClient.Connect(liveSchema, cancelToken: tokenSource)
                .ConfigureAwait(false);
            if (!isConnected)
                throw StandardError.External("Deepgram connection failed");

            // whenCompleted completes on the finalize ack, connection close, or error; with keepAlive
            // on, a lost ack would otherwise hang forever - so it must observe the token.
            await TranscriberHelper.WhenPushAndRead(
                    PushAudio(transcriptState, deepgramClient, tokenSource.Token),
                    whenCompleted.WaitAsync(tokenSource.Token),
                    tokenSource)
                .ConfigureAwait(false);

            try {
                // Ignore errors on stopping
                await deepgramClient.Stop(tokenSource).ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Error closing transcription stream #{StreamId}", audioStreamId);
            }
        }
        catch (Exception e) {
            error = e;
            Log.LogError(e, "Error transcribing #{StreamId}", audioStreamId);
            throw;
        }
        finally {
            output.TryComplete(error);
        }
        return;

        void HandleTranscriptReceived(object? sender, ResultResponse e)
            => ProcessResponse(transcriptState, whenCompletedSource, e, options);

        void HandleConnectionClosed(object? sender, CloseResponse e)
            => whenCompletedSource.TrySetResult();

        void HandleConnectionError(object? sender, ErrorResponse e)
            => whenCompletedSource.TrySetException(new TranscriptionException(e.Message, e.Description));
    }

    // Private methods

    private async Task PushAudio(
        DeepgramTranscribeState state,
        ListenWebSocketClient deepgramClient,
        CancellationToken cancellationToken)
    {
        var audioSource = state.AudioSource;
        try {
            // Add short silence at the start and end to help Deepgram finalize the last words
            var silenceAudio = await TranscriberHelper.GetSilenceAudioSource(Services).ConfigureAwait(false);
            var transcribedAudioSource = TranscriberHelper.AddSilentPrefixAndSuffix(
                audioSource,
                silenceAudio,
                SilentPrefixDuration,
                SilentSuffixDuration,
                cancellationToken);

            var byteFrameStream = OggOpusStreamConverter.ToByteFrameStream(transcribedAudioSource, cancellationToken);
            var clock = Clocks.CpuClock;
            var startedAt = clock.Now;
            var nextChunkAt = startedAt;
            await foreach (var (chunk, lastFrame) in byteFrameStream.ConfigureAwait(false)) {
                var delay = nextChunkAt - clock.Now;
                if (delay > TimeSpan.Zero)
                    await clock.Delay(delay, cancellationToken).ConfigureAwait(false);

                deepgramClient.Send(chunk);

                if (lastFrame != null) {
                    // Subtract the artificial silent prefix from timing to keep scheduler in sync with original audio
                    var processedAudioDuration =
                        (lastFrame.Offset + lastFrame.Duration - SilentPrefixDuration).Positive();
                    if (audioSource.WhenDurationAvailable.IsCompletedSuccessfully)
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
            await deepgramClient.SendFinalize().ConfigureAwait(false);
        }
    }

    private void ProcessResponse(
        DeepgramTranscribeState state,
        AsyncTaskMethodBuilder whenCompletedSource,
        ResultResponse result,
        TranscriptionOptions options)
    {
        DebugLog?.LogDebug("Transcript received: {Result}", result.ToString().ToPrivate());
        var isFinal = result.IsFinal ?? false;
        var isStreamFinalized = result.FromFinalize ?? false;
        var alternative = result.Channel?.Alternatives?.FirstOrDefault();
        var suffix = alternative?.Transcript ?? "";
        // NOTE: as for now deepgram does not support language detection in streaming mode,
        // so we use language from options
        var detectedLanguages = alternative?.Languages?.Select(DeepgramLanguage.FromDeepgram)
            .Distinct().ToArray() ?? [];
        var languages = detectedLanguages.Length > 0 ? detectedLanguages : [options.Language];
        // Snippets shorter than this are often misclassified, so detection waits for more context.
        const int minLanguageDetectionLength = 20;
        if (options.DetectLanguage && detectedLanguages.Length > 0
            && suffix.Length >= minLanguageDetectionLength)
            options.LanguageDetectedCallback?.Invoke(languages);
        var endTime = (float?)result.Duration ?? 0;
        if (isFinal) {
            if (TryParseFinal(state, result, out suffix, out var map))
                state.Append(suffix, map, languages).MakeStable();
            else
                state.MakeStable();
        }
        else
            state.Append(FixSuffix(state.Stable.Text, suffix), endTime, languages);

        if (state.Unstable.Length != 0)
            _ = state.Output.WriteAsync(state.Unstable);

        if (isStreamFinalized) {
            if (!CompleteSentenceOrEmptyRegex.IsMatch(state.Stable.Text))
                state.CompleteSentence();
            state.MakeStable();
            _ = state.Output.WriteAsync(state.Stable);
            whenCompletedSource.TrySetResult();
        }
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
        var endTime = (float?)result.Start + (float?)result.Duration ?? 0;
        if (alternative == null || alternative.Transcript.IsNullOrEmpty()) {
            text = "";
            return false;
        }

        text = FixSuffix(lastStable.Text, alternative.Transcript);
        var mapPoints = new List<Vector2>();
        var parsedOffset = 0;
        var parsedDuration = lastStableDuration;
        foreach (var word in alternative.Words ?? []) {
            var wordStartTime = (float?)word.Start ?? 0;
            if (wordStartTime < parsedDuration)
                continue;
            if (word.PunctuatedWord == null)
                continue;

            var wordStart = text.IndexOf(word.PunctuatedWord, parsedOffset, StringComparison.OrdinalIgnoreCase);
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

    private static bool IsNova3Language(Language language)
        => language.IsAnyEnglish || Nova3Languages.Contains(language);
}
