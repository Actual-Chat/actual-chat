using System.Numerics;
using System.Text.RegularExpressions;
using ActualChat.Audio;
using ActualChat.Module;
using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V2;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using EndpointingSensitivity = Google.Cloud.Speech.V2.StreamingRecognitionFeatures.Types.EndpointingSensitivity;
using static ActualChat.Constants.Transcription.Google;

namespace ActualChat.Transcription;

public sealed partial class GoogleTranscriber : ITranscriber
{
    public sealed class Options
    {
        public string Model { get; set; } = Constants.Transcription.Google.Model;
        public EndpointingSensitivity EndpointingSensitivity { get; set; } = DefaultEndpointingSensitivity;
    }

    // Short and Supershort are documented as latency optimizations, but measured on a 70s ru
    // recording both fragment the stream badly enough to lose the tail: 394/368 chars vs 575.
    public const EndpointingSensitivity DefaultEndpointingSensitivity = EndpointingSensitivity.Standard;

    [GeneratedRegex(@"([\?\!\.]\s*$)|(^\s*$)", RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex CompleteSentenceOrEmptyRegexFactory();

    [GeneratedRegex(@"(\s+$)|(^\s*$)", RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex EndsWithWhitespaceOrEmptyRegexFactory();

    private static readonly Regex CompleteSentenceOrEmptyRegex = CompleteSentenceOrEmptyRegexFactory();
    private static readonly Regex EndsWithWhitespaceOrEmptyRegex = EndsWithWhitespaceOrEmptyRegexFactory();

    private readonly Options _options;

    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private static bool DebugMode => Constants.DebugMode.TranscriberGoogle || Constants.DebugMode.TranscriberAny;

    private IServiceProvider Services { get; }
    private CoreServerSettings CoreServerSettings { get; }
    private MomentClockSet Clocks { get; }
    private WebMStreamConverter WebMStreamConverter { get; }
    private OggOpusStreamConverter OggOpusStreamConverter { get; }

    private SpeechClient SpeechClient { get; set; } = null!; // Post-WhenInitialized
    private string GoogleProjectId { get; set; } = null!; // Post-WhenInitialized
    private string RegionId => CoreServerSettings.GoogleRegionId;

    public Task WhenInitialized { get; }
    public TranscriberInfo Info { get; } = new() {
        Id = TranscriberId.GoogleStream,
        Kind = TranscriberKind.Stream,
    };

    public GoogleTranscriber(IServiceProvider services, Options? options = null)
    {
        _options = options ?? new Options();
        Services = services;
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();

        CoreServerSettings = services.GetRequiredService<CoreServerSettings>();
        WebMStreamConverter = new WebMStreamConverter(Clocks, services.LogFor<WebMStreamConverter>());
        OggOpusStreamConverter = new OggOpusStreamConverter(new OggOpusStreamConverter.Options {
            PageDuration = Constants.Transcription.StreamPageDuration,
        });
        WhenInitialized = Initialize();
    }

    private async Task Initialize()
    {
        var speechClientBuilder = new SpeechClientBuilder {
            // See https://cloud.google.com/speech-to-text/v2/docs/speech-to-text-supported-languages
            Endpoint = $"{RegionId}-speech.googleapis.com:443",
        };

        // Start a few tasks in parallel
        var speechClientTask = speechClientBuilder.BuildAsync();

        if (!CoreServerSettings.GoogleProjectId.IsNullOrEmpty())
            GoogleProjectId = CoreServerSettings.GoogleProjectId;
        else {
            var platform = await Platform.InstanceAsync().ConfigureAwait(false);
            GoogleProjectId = platform?.ProjectId ?? throw StandardError.NotSupported<GoogleTranscriber>(
                $"Requires GKE or explicit settings of {nameof(CoreServerSettings)}." +
                $"{nameof(CoreServerSettings.GoogleProjectId)}");
        }

        if (GoogleProjectId != "n/a")
            SpeechClient = await speechClientTask.ConfigureAwait(false);
    }

    public async Task Transcribe(
        string audioStreamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        ChannelWriter<Transcript> output,
        CancellationToken cancellationToken = default)
    {
        try {
            await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
            Log.LogDebug("Starting recognize process for {AudioStreamId}", audioStreamId);

            var languageCode = GetRecognizerOptions(options.Language).LanguageCode;
            var recognizerId = $"{languageCode.ToLower()}-{_options.Model.Replace('_', '-')}";
            var parent = $"projects/{GoogleProjectId}/locations/{RegionId}";
            var recognizerName = $"{parent}/recognizers/{recognizerId}";

            try {
                await TranscribeInternal(recognizerName, audioSource, options, output, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RpcException e) when (
                e.StatusCode is StatusCode.NotFound
                && e.Status.Detail.StartsWith("Unable to find Recognizer"))
            {
                await CreateRecognizer(recognizerId, parent, options, cancellationToken)
                    .ConfigureAwait(false);
                await TranscribeInternal(recognizerName, audioSource, options, output, cancellationToken)
                    .ConfigureAwait(false);
            }

            output.TryComplete();
        }
        catch (Exception e) {
            output.TryComplete(e);
            throw;
        }
    }

    // Protected/internal methods

    // It's internal to be accessible from tests
    internal async IAsyncEnumerable<Transcript> ProcessResponses(
        GoogleTranscribeState state,
        TranscriptionOptions options,
        IAsyncEnumerable<StreamingRecognizeResponse> responses)
    {
        await foreach (var response in responses.ConfigureAwait(false)) {
            var transcript = ProcessResponse(state, options, response);
            if (transcript != null)
                yield return transcript;
        }

        state.MakeStable();
        var finalTranscript = state.Stable.WithSuffix("", state.ProcessedAudioDuration);
        yield return finalTranscript;
    }

    // Private methods

    private StreamingRecognitionConfig GetStreamingRecognitionConfig(TranscriptionOptions options)
    {
        var languageCode = GetRecognizerOptions(options.Language).LanguageCode;
        return new StreamingRecognitionConfig {
            Config = new RecognitionConfig {
                Model = _options.Model,
                LanguageCodes = { languageCode },
                AutoDecodingConfig = new AutoDetectDecodingConfig(),
                // Word timings and confidences go unread - the time map is built from
                // ResultEndOffset - and asking for them cost 33% of the transcribed text.
                Features = new RecognitionFeatures {
                    EnableAutomaticPunctuation = true,
                },
            },
            StreamingFeatures = new StreamingRecognitionFeatures {
                InterimResults = true,
                EndpointingSensitivity = _options.EndpointingSensitivity,
                // TODO(AK): test google VAD events - might be useful
                // VoiceActivityTimeout =
                // EnableVoiceActivityEvents =
            },
        };
    }

    private async Task TranscribeInternal(
        string recognizerName,
        AudioSource audioSource,
        TranscriptionOptions options,
        ChannelWriter<Transcript> output,
        CancellationToken cancellationToken)
    {
        // We want to stop both tasks here on any failure, so...
#pragma warning disable CA2000
        var cts = cancellationToken.CreateLinkedTokenSource();
#pragma warning restore CA2000
        // The call itself must be bound to cts as well: PullResponses reads its response stream
        // with no token of its own, so cancelling the call is the only way to unblock it.
        var recognizeStream = SpeechClient.StreamingRecognize(
            CallSettings.FromCancellationToken(cts.Token),
            new BidirectionalStreamingSettings(1));
        await recognizeStream.WriteAsync(new StreamingRecognizeRequest {
            StreamingConfig = GetStreamingRecognitionConfig(options),
            Recognizer = recognizerName,
        }).ConfigureAwait(false);

        var state = new GoogleTranscribeState(audioSource, options, recognizeStream, output);
        try {
            await TranscriberHelper.WhenPushAndRead(
                    PushAudio(state, cts.Token),
                    PullResponses(state, options, cts.Token),
                    cts)
                .ConfigureAwait(false);
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }

    private async Task PushAudio(
        GoogleTranscribeState state,
        CancellationToken cancellationToken)
    {
        var audioSource = state.AudioSource;
        var recognizeStream = state.RecognizeStream;
        try {
            // Add short silence at the start and end to help transcriber to finalize the last words
            var silenceAudio = await TranscriberHelper.GetSilenceAudioSource(Services).ConfigureAwait(false);
            var transcribedAudioSource = TranscriberHelper.AddSilentPrefixAndSuffix(
                audioSource,
                silenceAudio,
                SilentPrefixDuration,
                SilentSuffixDuration,
                cancellationToken);

            var streamConverter = (IAudioStreamConverter) (IsWebMOpusEnabled
                ? WebMStreamConverter
                : OggOpusStreamConverter);
            var byteFrameStream = streamConverter.ToByteFrameStream(transcribedAudioSource, cancellationToken);
            var clock = Clocks.CpuClock;
            var startedAt = clock.Now;
            var nextChunkAt = startedAt;
            await foreach (var (chunk, lastFrame) in byteFrameStream.ConfigureAwait(false)) {
                var delay = nextChunkAt - clock.Now;
                if (delay > TimeSpan.Zero)
                    await clock.Delay(delay, cancellationToken).ConfigureAwait(false);

                // StreamingConfig and Audio share a protobuf oneof: only the first request carries one.
                var request = new StreamingRecognizeRequest {
                    Audio = Google.Protobuf.ByteString.CopyFrom(chunk),
                };
                await recognizeStream.WriteAsync(request).ConfigureAwait(false);

                if (lastFrame != null) {
                    var processedAudioDuration =
                        (lastFrame.Offset + lastFrame.Duration - SilentPrefixDuration).Positive();
                    if (audioSource.WhenDurationAvailable.IsCompletedSuccessfully)
                        processedAudioDuration = TimeSpanExt.Min(audioSource.Duration, processedAudioDuration);
                    state.ProcessedAudioDuration = (float)processedAudioDuration.TotalSeconds;
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
            // Google ends the response stream (unblocking PullResponses) only after the write side completes
            await recognizeStream.TryWriteCompleteAsync().SilentAwait(false);
        }
    }

    private async Task PullResponses(
        GoogleTranscribeState state,
        TranscriptionOptions options,
        CancellationToken cancellationToken)
    {
        // NOTE(AY): This method isn't supposed to complete the output: this part is done in Transcribe
        try {
            var output = state.Output;
            var responses = (IAsyncEnumerable<StreamingRecognizeResponse>)state.RecognizeStream.GetResponseStream();
            await output.WriteAsync(state.Unstable, cancellationToken).ConfigureAwait(false);
            // ReSharper disable once UseCancellationTokenForIAsyncEnumerable
            await foreach (var transcript in ProcessResponses(state, options, responses).ConfigureAwait(false))
                await output.WriteAsync(transcript, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, $"{nameof(PullResponses)} failed");
            throw;
        }
    }

    private Transcript? ProcessResponse(
        GoogleTranscribeState state,
        TranscriptionOptions options,
        StreamingRecognizeResponse response)
    {
        var results = response.Results;
        DebugLog?.LogDebug("Got response with {ResultCount} results", results.Count);

        var lastStability = state.Stability;
        var stability = results
            .Select(r => r.Stability)
            .ToArray();
        var isFinal = results.Any(r => r.IsFinal);

        if (UseStabilityHeuristics) {
            // Google Transcribe issue: sometimes it omits the final transcript,
            // so we use a heuristic to automatically identify it
            if (!isFinal && stability.Length == 1 && stability[0] < 0.5
                && lastStability.Length > 1 && lastStability.Max() > 0.5)
                // Not marked as final, but:
                // - Previous results contain at least one having a high stability,
                // - And now we're getting a single result with a low stability.
                state.MakeStable();
        }

        var mustAppendToUnstable = false;
        for (var i = 0; i < results.Count; i++) {
            var result = results[i];
            DebugLog?.LogDebug("Result {Index}: {Result}", i, result.ToString().ToPrivate());
            ProcessResult(state, result, options, mustAppendToUnstable);
            mustAppendToUnstable |= !result.IsFinal;
            DebugLog?.LogDebug("Transcript {Index}: {Transcript}", i, state.Unstable);
        }

        state.SetMetadata(stability);
        return state.Unstable;
    }

    private static void ProcessResult(
        GoogleTranscribeState state,
        StreamingRecognitionResult result,
        TranscriptionOptions options,
        bool mustAppendToUnstable)
    {
        var isFinal = result.IsFinal;
        var suffix = result.Alternatives.FirstOrDefault()?.Transcript ?? "";
        var endTime = TryGetOriginalAudioTime(result.ResultEndOffset) ?? state.ProcessedAudioDuration;
        suffix = FixSuffix(state[mustAppendToUnstable].Text, suffix);

        // Google transcriber sometimes returns empty final transcript -
        // we assume that the last unstable one becomes stable in this case.
        if (isFinal && suffix.IsNullOrWhiteSpace()) {
            state.MakeStable();
            return;
        }

#if false
        // Google Transcribe issue: sometimes it omits the final transcript,
        // so we use a heuristic to automatically identify it
        var transcript = state.Unstable;
        if (!ReferenceEquals(transcript, state.Stable)) {
            var lastEndTime = transcript.TimeMap.YRange.End;
            if (endTime - lastEndTime >= 0.5f) {
                // AND there is > .5s delay between the new unstable piece and the old one
                var lastSuffixLength = transcript.Length - state.Stable.Length;
                var minValidSuffixLength = Math.Max(0, lastSuffixLength - 4);
                if (suffix.Length < minValidSuffixLength) {
                    // AND the suffix is shorter than the (lastSuffix.Length - 4)
                    state.MakeStable();
                }
            }
        }
#endif

        // Sometimes Final response seems to be cut, let's check its size and keep unstable transcription if necessary
        if (isFinal && !mustAppendToUnstable)
            // Unstable is significantly larger than new stable chunk
            if (state.Unstable.Length > suffix.Length + 8) {
                state.MakeStable();
                return;
            }

        var languages = Language.TryParse(result.LanguageCode, out var language)
            ? new [] {language}
            : [];
        if (languages.Length == 0)
            languages = [options.Language];
        state.Append(suffix, endTime, languages, mustAppendToUnstable).MakeStable(isFinal);
    }

    // This method is unused for now, since we rely on our own time offset computation logic
    private static bool TryParseFinal(
        GoogleTranscribeState state,
        StreamingRecognitionResult result,
        out string text,
        out LinearMap timeMap)
    {
        var lastStable = state.Stable;
        var lastStableTextLength = lastStable.Text.Length;
        var lastStableDuration = lastStable.TimeMap.YRange.End;

        var alternative = result.Alternatives.Single();
        var resultEndOffset = result.ResultEndOffset;
        var endTime = resultEndOffset == null
            ? null
            : (float?) resultEndOffset.ToTimeSpan().TotalSeconds;
        text = alternative.Transcript;
        if (lastStableTextLength > 0 && text.Length > 0 && !char.IsWhiteSpace(text[0]))
            text = " " + text;

        var mapPoints = new List<Vector2>();
        var parsedOffset = 0;
        var parsedDuration = lastStableDuration;
        foreach (var word in alternative.Words) {
            var wordStartTime = word.StartOffset == null
                ? 0
                : (float)word.StartOffset.ToTimeSpan().TotalSeconds;
            if (wordStartTime < parsedDuration)
                continue;
            var wordStart = text.IndexOf(word.Word, parsedOffset, StringComparison.OrdinalIgnoreCase);
            if (wordStart < 0)
                continue;

            var wordEndTime = (float)word.EndOffset.ToTimeSpan().TotalSeconds;
            var wordEnd = wordStart + word.Word.Length;

            mapPoints.Add(new Vector2(lastStableTextLength + wordStart, GetOriginalAudioTime(wordStartTime)));
            mapPoints.Add(new Vector2(lastStableTextLength + wordEnd, GetOriginalAudioTime(wordEndTime)));

            parsedDuration = wordStartTime;
            parsedOffset = wordStart + word.Word.Length;
        }

        if (mapPoints.Count == 0) {
            timeMap = default;
            return false;
        }

        var lastPoint = mapPoints[^1];
        var veryLastPoint = new Vector2(lastStableTextLength + text.Length, endTime ?? mapPoints.Max(v => v.Y));
        if (Math.Abs(lastPoint.X - veryLastPoint.X) < 0.1)
            mapPoints[^1] = veryLastPoint;
        else
            mapPoints.Add(veryLastPoint);
        timeMap = new LinearMap(mapPoints.ToArray()).Simplify(Transcript.TimeMapEpsilon);
        return true;
    }

    private async Task CreateRecognizer(
        string recognizerId,
        string recognizerParent,
        TranscriptionOptions options,
        CancellationToken cancellationToken)
    {
        Log.LogWarning("Creating new recognizer: #{RecognizerId}", recognizerId);
        var recognizerOptions = GetRecognizerOptions(options.Language);
        var createRecognizerRequest = new CreateRecognizerRequest {
            Parent = recognizerParent,
            RecognizerId = recognizerId,
            Recognizer = new Recognizer {
#pragma warning disable CS0612 // Type or member is obsolete -
                // NOTE(AY): We anyway using the newer properties, search for "RecognitionConfig" here
                Model = _options.Model,
                DisplayName = recognizerId,
                LanguageCodes = { recognizerOptions.LanguageCode },
#pragma warning restore CS0612 // Type or member is obsolete
                DefaultRecognitionConfig = new RecognitionConfig {
                    Features = new RecognitionFeatures {
                        EnableAutomaticPunctuation = recognizerOptions.EnableAutomaticPunctuation,
                        MaxAlternatives = 0,
                        EnableSpokenPunctuation = false,
                        EnableSpokenEmojis = false,
                        ProfanityFilter = recognizerOptions.ProfanityFilter,
                        EnableWordConfidence = false,
                        EnableWordTimeOffsets = false,
                        MultiChannelMode = RecognitionFeatures.Types.MultiChannelMode.Unspecified,
                    },
                    AutoDecodingConfig = new AutoDetectDecodingConfig(),
                },
            },
        };
        var createRecognizerOperation = await SpeechClient
            .CreateRecognizerAsync(createRecognizerRequest, cancellationToken)
            .ConfigureAwait(false);
        var createRecognizerCompleted = await createRecognizerOperation
            .PollUntilCompletedAsync(null, CallSettings.FromCancellationToken(cancellationToken))
            .ConfigureAwait(false);
        var result = createRecognizerCompleted.Result;
        Log.LogWarning("Creating new recognizer: #{RecognizerId} -> {Result}", recognizerId, result);
    }

    private static RecognizerOptions GetRecognizerOptions(Language language)
    {
        // Defined based on https://cloud.google.com/speech-to-text/v2/docs/speech-to-text-supported-languages
        var isAutomaticPunctuationSupported =
            language != Languages.EnglishIN &&
            language != Languages.FrenchCA &&
            language != Languages.Portuguese &&
            language != Languages.Ukrainian &&
            language != Languages.Turkish &&
            language != Languages.Thai &&
            language != Languages.Polish;
        var isProfanityFilterEnabled =
            language != Languages.FrenchCA;
        return new RecognizerOptions(language.Value, isAutomaticPunctuationSupported, isProfanityFilterEnabled);
    }

    private static string FixSuffix(string prefix, string suffix)
    {
        /*
        // Trim trailing whitespace
        var lastLetterIndex = suffix.Length - Transcript.ContentStartRegex.Match(suffix).Length;
        suffix = suffix[..lastLetterIndex];
        */

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

    private static float? TryGetOriginalAudioTime(Duration? time)
        => time is { } vTime ? GetOriginalAudioTime(vTime) : null;
    private static float? TryGetOriginalAudioTime(float? time)
        => time is { } vTime ? GetOriginalAudioTime(vTime) : null;
    private static float GetOriginalAudioTime(Duration time)
        => GetOriginalAudioTime((float)time.ToTimeSpan().TotalSeconds);
    private static float GetOriginalAudioTime(float time)
        => (float)Math.Round(Math.Max(0, time - SilentPrefixDuration.TotalSeconds), 2);

    // Nested types

    private record struct RecognizerOptions(string LanguageCode, bool EnableAutomaticPunctuation, bool ProfanityFilter);
}
