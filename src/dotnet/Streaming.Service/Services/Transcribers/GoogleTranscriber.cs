using System.Numerics;
using System.Text.RegularExpressions;
using ActualChat.Audio;
using ActualChat.Module;
using ActualChat.Transcription;
using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V2;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using static ActualChat.Constants.Transcription.Google;

namespace ActualChat.Streaming.Services.Transcribers;

public partial class GoogleTranscriber : ITranscriber
{
    [GeneratedRegex(@"([\?\!\.]\s*$)|(^\s*$)", RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex CompleteSentenceOrEmptyRegexFactory();

    [GeneratedRegex(@"(\s+$)|(^\s*$)", RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex EndsWithWhitespaceOrEmptyRegexFactory();

    private static readonly Regex CompleteSentenceOrEmptyRegex = CompleteSentenceOrEmptyRegexFactory();
    private static readonly Regex EndsWithWhitespaceOrEmptyRegex = EndsWithWhitespaceOrEmptyRegexFactory();
    private static readonly string RegionId = "us";

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
    private AudioSource SilenceAudioSource { get; set; } = null!; // Post-WhenInitialized

    public Task WhenInitialized { get; }

    public GoogleTranscriber(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();

        CoreServerSettings = services.GetRequiredService<CoreServerSettings>();
        WebMStreamConverter = new WebMStreamConverter(Clocks, services.LogFor<WebMStreamConverter>());
        OggOpusStreamConverter = new OggOpusStreamConverter();
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
        var loadSilenceAudioTask = LoadSilenceAudio();

        if (!CoreServerSettings.GoogleProjectId.IsNullOrEmpty())
            GoogleProjectId = CoreServerSettings.GoogleProjectId;
        else {
            var platform = await Platform.InstanceAsync().ConfigureAwait(false);
            GoogleProjectId = platform?.ProjectId ?? throw StandardError.NotSupported<GoogleTranscriber>(
                $"Requires GKE or explicit settings of {nameof(CoreServerSettings)}.{nameof(CoreServerSettings.GoogleProjectId)}");
        }

        if (!OrdinalEquals(GoogleProjectId, "n/a"))
            SpeechClient = await speechClientTask.ConfigureAwait(false);
        SilenceAudioSource = await loadSilenceAudioTask.ConfigureAwait(false);
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
            var recognizerId = $"{languageCode.ToLowerInvariant()}";
            var parent = $"projects/{GoogleProjectId}/locations/{RegionId}";
            var recognizerName = $"{parent}/recognizers/{recognizerId}";

            try {
                await TranscribeInternal(recognizerName, audioSource, options, output, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RpcException e) when (
                e.StatusCode is StatusCode.NotFound
                && e.Status.Detail.OrdinalStartsWith("Unable to find Recognizer"))
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

    // Private methods

    private StreamingRecognitionConfig GetStreamingRecognitionConfig(TranscriptionOptions options)
    {
        var languageCode = GetRecognizerOptions(options.Language).LanguageCode;
        return new StreamingRecognitionConfig {
            Config = new RecognitionConfig {
                Model = "long",
                LanguageCodes = { languageCode },
                AutoDecodingConfig = new AutoDetectDecodingConfig(),
                Features = new RecognitionFeatures {
                    EnableAutomaticPunctuation = true,
                    EnableWordConfidence = true,
                    EnableWordTimeOffsets = true,
                },
            },
            StreamingFeatures = new StreamingRecognitionFeatures {
                InterimResults = true,
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
        var recognizeStream = SpeechClient.StreamingRecognize(
            CallSettings.FromCancellationToken(cancellationToken),
            new BidirectionalStreamingSettings(1));
        await recognizeStream.WriteAsync(new StreamingRecognizeRequest {
            StreamingConfig = GetStreamingRecognitionConfig(options),
            Recognizer = recognizerName,
        }).ConfigureAwait(false);

        var state = new GoogleTranscribeState(audioSource, options, recognizeStream, output);
        // We want to stop both tasks here on any failure, so...
#pragma warning disable CA2000
        var cts = cancellationToken.CreateLinkedTokenSource();
#pragma warning restore CA2000
        try {
            var pushAudioTask = PushAudio(state, cts.Token);
            var pullResponsesTask = PullResponses(state, cts.Token);
            await pushAudioTask.ConfigureAwait(false);
            await pullResponsesTask.ConfigureAwait(false);
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
            var transcribedAudioSource = AddSilentPrefixAndSuffix(audioSource, cancellationToken);
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

                var request = new StreamingRecognizeRequest {
                    StreamingConfig = GetStreamingRecognitionConfig(state.Options),
                    Audio = ByteString.CopyFrom(chunk),
                };
                await recognizeStream.WriteAsync(request).ConfigureAwait(false);

                if (lastFrame != null) {
                    var processedAudioDuration = (lastFrame.Offset + lastFrame.Duration - SilentPrefixDuration).Positive();
                    if (audioSource.WhenDurationAvailable.IsCompletedSuccessfully())
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
            _ = recognizeStream.TryWriteCompleteAsync();
        }
    }

    private async Task PullResponses(
        GoogleTranscribeState state,
        CancellationToken cancellationToken)
    {
        // NOTE(AY): This method isn't supposed to complete the output: this part is done in Transcribe
        try {
            var output = state.Output;
            var responses = (IAsyncEnumerable<StreamingRecognizeResponse>)state.RecognizeStream.GetResponseStream();
            await output.WriteAsync(state.Unstable, cancellationToken).ConfigureAwait(false);
            // ReSharper disable once UseCancellationTokenForIAsyncEnumerable
            await foreach (var transcript in ProcessResponses(state, responses).ConfigureAwait(false))
                await output.WriteAsync(transcript, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, $"{nameof(PullResponses)} failed");
            throw;
        }
    }

    // It's internal to be accessible from tests
    internal async IAsyncEnumerable<Transcript> ProcessResponses(
        GoogleTranscribeState state,
        IAsyncEnumerable<StreamingRecognizeResponse> responses)
    {
        await foreach (var response in responses.ConfigureAwait(false)) {
            var transcript = ProcessResponse(state, response);
            if (transcript != null)
                yield return transcript;
        }

        state.MakeStable();
        var finalTranscript = state.Stable;
        if (string.IsNullOrWhiteSpace(finalTranscript.Text))
            finalTranscript = Transcript.Unrecognized;
        finalTranscript = finalTranscript.WithSuffix("", state.ProcessedAudioDuration);
        yield return finalTranscript;
    }

    private Transcript? ProcessResponse(
        GoogleTranscribeState state,
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
            if (!isFinal && stability.Length == 1 && stability[0] < 0.5 && lastStability.Length > 1 && lastStability.Max() > 0.5)
                // Not marked as final, but:
                // - Previous results contain at least one having a high stability,
                // - And now we're getting a single result with a low stability.
                state.MakeStable();
        }

        var mustAppendToUnstable = false;
        for (var i = 0; i < results.Count; i++) {
            var result = results[i];
            DebugLog?.LogDebug("Result {Index}: {Result}", i, result);
            ProcessResult(state, result, mustAppendToUnstable);
            mustAppendToUnstable |= !result.IsFinal;
            DebugLog?.LogDebug("Transcript {Index}: {Transcript}", i, state.Unstable);
        }

        state.SetMetadata(stability);
        return state.Unstable;
    }

    private static void ProcessResult(GoogleTranscribeState state, StreamingRecognitionResult result, bool appendToUnstable)
    {
        var isFinal = result.IsFinal;
        var suffix = result.Alternatives.FirstOrDefault()?.Transcript ?? "";
        var endTime = TryGetOriginalAudioTime(result.ResultEndOffset) ?? state.ProcessedAudioDuration;
        suffix = FixSuffix(state[appendToUnstable].Text, suffix);

        // Google transcriber sometimes returns empty final transcript -
        // we assume that the last unstable one becomes stable in this case.
        if (isFinal && string.IsNullOrWhiteSpace(suffix)) {
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
        if (isFinal && !appendToUnstable)
            // Unstable is significantly larger than new stable chunk
            if (state.Unstable.Length > suffix.Length + 8) {
                state.MakeStable();
                return;
            }

        state.Append(suffix, endTime, appendToUnstable).MakeStable(isFinal);
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
            var wordStart = text.OrdinalIgnoreCaseIndexOf(word.Word, parsedOffset);
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

    // Helpers

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
                Model = "long",
                DisplayName = recognizerId,
                LanguageCodes = { recognizerOptions.LanguageCode },
                DefaultRecognitionConfig = new RecognitionConfig {
                    Features = new RecognitionFeatures {
                        EnableAutomaticPunctuation = recognizerOptions.EnableAutomaticPunctuation,
                        MaxAlternatives = 0,
                        EnableSpokenPunctuation = false,
                        EnableSpokenEmojis = false,
                        ProfanityFilter = recognizerOptions.ProfanityFilter,
                        EnableWordConfidence = false,
                        EnableWordTimeOffsets = true,
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
        bool supportAutomaticPunctuation =
            language != Languages.EnglishIN &&
            language != Languages.FrenchCA &&
            language != Languages.Portuguese &&
            language != Languages.Ukrainian &&
            language != Languages.Turkish &&
            language != Languages.Thai &&
            language != Languages.Polish;
        bool profanityFilter =
            language != Languages.FrenchCA;
        return new RecognizerOptions(language.Value, supportAutomaticPunctuation, profanityFilter);
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

    private AudioSource AddSilentPrefixAndSuffix(AudioSource audioSource, CancellationToken cancellationToken)
        => SilenceAudioSource
            .Take(SilentPrefixDuration, cancellationToken)
            .Concat(audioSource, cancellationToken)
            .ConcatUntil(SilenceAudioSource, SilentSuffixDuration, cancellationToken);

    private async Task<AudioSource> LoadSilenceAudio()
    {
        var silenceChunks = await typeof(GoogleTranscriber).Assembly
            .GetManifestResourceStream("ActualChat.Streaming.data.silence.opuss")!
            .ReadByteStream(true)
            .ToListAsync()
            .ConfigureAwait(false);
        var converter = new ActualOpusStreamConverter(
            MomentClockSet.Default,
            Services.LogFor<ActualOpusStreamConverter>());
        return await converter
            .FromByteStream(silenceChunks.AsAsyncEnumerable(), CancellationToken.None)
            .ConfigureAwait(false);
    }

    private record struct RecognizerOptions(string LanguageCode, bool EnableAutomaticPunctuation, bool ProfanityFilter);
}
