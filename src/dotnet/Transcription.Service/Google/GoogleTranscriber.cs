using System.Numerics;
using System.Text.RegularExpressions;
using ActualChat.Audio;
using ActualChat.Module;
using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V2;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace ActualChat.Transcription.Google;

public class GoogleTranscriber : ITranscriber
{
    private static readonly Regex CompleteSentenceOrEmptyRe =
        new(@"([\?\!\.]\s*$)|(^\s*$)", RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.Singleline);
    private static readonly Regex EndsWithWhitespaceOrEmptyRe =
        new(@"(\s+$)|(^\s*$)", RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.Singleline);

    private static readonly string RegionId = "us";
    private static readonly TimeSpan SilentPrefixDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SilentSuffixDuration = TimeSpan.FromSeconds(4);
    private static readonly double TranscriptionSpeed = 2;

    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.TranscriberGoogle || Constants.DebugMode.TranscriberAny;

    private IServiceProvider Services { get; }
    private CoreSettings CoreSettings { get; }
    private MomentClockSet Clocks { get; }
    private WebMStreamConverter WebMStreamConverter { get; }

    private SpeechClient SpeechClient { get; set; } = null!; // Post-WhenInitialized
    private StreamingRecognitionConfig RecognitionConfig { get; set; } = null!; // Post-WhenInitialized
    private string GoogleProjectId { get; set; } = null!; // Post-WhenInitialized
    private AudioSource SilenceAudioSource { get; set; } = null!; // Post-WhenInitialized

    public Task WhenInitialized { get; }

    public GoogleTranscriber(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Clocks = services.GetRequiredService<MomentClockSet>();

        Services = services;
        CoreSettings = services.GetRequiredService<CoreSettings>();
        WebMStreamConverter = new WebMStreamConverter(Clocks, services.LogFor<WebMStreamConverter>());
        WhenInitialized = Initialize();
    }

    private async Task Initialize()
    {
        RecognitionConfig = new StreamingRecognitionConfig {
            Config = new RecognitionConfig {
                AutoDecodingConfig = new AutoDetectDecodingConfig(),
            },
            StreamingFeatures = new StreamingRecognitionFeatures {
                InterimResults = true,
                // TODO(AK): test google VAD events - probably it might be useful
                // VoiceActivityTimeout =
                // EnableVoiceActivityEvents =
            },
        };

        var speechClientBuilder = new SpeechClientBuilder {
            // See https://cloud.google.com/speech-to-text/v2/docs/speech-to-text-supported-languages
            Endpoint = $"{RegionId}-speech.googleapis.com:443",
        };

        // Start a few tasks in parallel
        var speechClientTask = speechClientBuilder.BuildAsync();
        var loadSilenceAudioTask = LoadSilenceAudio();

        if (!CoreSettings.GoogleProjectId.IsNullOrEmpty())
            GoogleProjectId = CoreSettings.GoogleProjectId;
        else {
            var platform = await Platform.InstanceAsync().ConfigureAwait(false);
            GoogleProjectId = platform?.ProjectId ?? throw StandardError.NotSupported<GoogleTranscriber>(
                $"Requires GKE or explicit settings of {nameof(CoreSettings)}.{nameof(CoreSettings.GoogleProjectId)}");
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

            var languageCode = GetLanguageCode(options.Language);
            var recognizerId = $"{languageCode.ToLowerInvariant()}";
            var parent = $"projects/{GoogleProjectId}/locations/{RegionId}";
            var recognizerName = $"{parent}/recognizers/{recognizerId}";

            try {
                await Transcribe(recognizerName, audioSource, output, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RpcException e) when (
                e.StatusCode is StatusCode.NotFound
                && e.Status.Detail.OrdinalStartsWith("Unable to find Recognizer"))
            {
                await CreateRecognizer(recognizerId, parent, options, cancellationToken)
                    .ConfigureAwait(false);
                await Transcribe(recognizerName, audioSource, output, cancellationToken)
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

    private async Task Transcribe(
        string recognizerName,
        AudioSource audioSource,
        ChannelWriter<Transcript> output,
        CancellationToken cancellationToken)
    {
        var recognizeStream = SpeechClient.StreamingRecognize(
            CallSettings.FromCancellationToken(cancellationToken),
            new BidirectionalStreamingSettings(1));
        await recognizeStream.WriteAsync(new StreamingRecognizeRequest {
            StreamingConfig = RecognitionConfig,
            Recognizer = recognizerName,
        }).ConfigureAwait(false);

        var state = new GoogleTranscribeState(audioSource, recognizeStream, output);
        // We want to stop both tasks here on any failure, so...
        var cts = cancellationToken.CreateLinkedTokenSource();
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
            var byteFrameStream = WebMStreamConverter.ToByteFrameStream(transcribedAudioSource, cancellationToken);
            var clock = Clocks.CpuClock;
            var startedAt = clock.Now;
            var nextChunkAt = startedAt;
            await foreach (var (chunk, lastFrame) in byteFrameStream.ConfigureAwait(false)) {
                var delay = nextChunkAt - clock.Now;
                if (delay > TimeSpan.Zero)
                    await clock.Delay(delay, cancellationToken).ConfigureAwait(false);

                var request = new StreamingRecognizeRequest {
                    StreamingConfig = RecognitionConfig,
                    Audio = ByteString.CopyFrom(chunk),
                };
                await recognizeStream.WriteAsync(request).ConfigureAwait(false);

                if (lastFrame != null) {
                    var processedAudioDuration = (lastFrame.Offset + lastFrame.Duration - SilentPrefixDuration).Positive();
                    if (audioSource.WhenDurationAvailable.IsCompletedSuccessfully())
                        processedAudioDuration = TimeSpanExt.Min(audioSource.Duration, processedAudioDuration);
                    state.ProcessedAudioDuration = (float)processedAudioDuration.TotalSeconds;
                    nextChunkAt = startedAt
                        + TimeSpan.FromSeconds(processedAudioDuration.TotalSeconds / TranscriptionSpeed)
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

        var finalTranscript = state.Stabilize().WithSuffix("", state.ProcessedAudioDuration);
        yield return finalTranscript;
    }

    private Transcript? ProcessResponse(
        GoogleTranscribeState state,
        StreamingRecognizeResponse response)
    {
        DebugLog?.LogDebug("Response={Response}", response);

        var results = response.Results;
        var isStable = results.Any(r => r.IsFinal);
        var fragments = isStable
            ? results.Where(r => r.IsFinal)
            : results;
        // google transcriber can return final result without transcript (alternatives)
        var text = fragments
            .Select(r => r.Alternatives.Count > 0
                ? r.Alternatives.First().Transcript
                : "")
            .ToDelimitedString("");
        var endTime = TryGetOriginalAudioTime(results.Last().ResultEndOffset) ?? state.ProcessedAudioDuration;
        Transcript? transcript = null;
        if (string.IsNullOrWhiteSpace(text)) {
            if (!isStable)
                return null;

            transcript = state.Stabilize();
        }
        else {
            text = FixSuffix(state.Stable.Text, text);

            // Google Transcribe issue: sometimes it omits the final transcript,
            // so we use a heuristic to automatically mark it stable
            if (state.Unstable != state.Stable && !isStable) {
                var lastEndTime = state.Unstable.TimeMap.YRange.End;
                if (endTime - lastEndTime > 0.25f) {
                    // AND there is > .25s delay between the new unstable piece and the old one
                    var legitLengthRatio = (endTime - lastEndTime) switch {
                        > 1f => 0.9f, // Longer delay => smaller trim allowed
                        > 0.5f => 0.75f,
                        _ => 0.6f, // Shorter delay => bigger trim allowed
                    };
                    var lastLength = state.Unstable.Length - state.Stable.Length;
                    var legitLength = Math.Min(
                        Math.Max(0, lastLength - 4), // Trimming by 4 is always legit
                        (int)(lastLength * legitLengthRatio));
                    if (text.Length < legitLength)
                        state.Stabilize();
                }
            }
            transcript = state.Append(isStable, text, endTime);
        }

        DebugLog?.LogDebug("Transcript={Transcript}, EndTime={EndTime}", transcript, endTime);
        return transcript;
    }

    // This method is unused for now, since we rely on our own time offset computation logic
    private bool TryParseFinal(
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
        var languageCode = GetLanguageCode(options.Language);
        var createRecognizerRequest = new CreateRecognizerRequest {
            Parent = recognizerParent,
            RecognizerId = recognizerId,
            Recognizer = new Recognizer {
                Model = "latest_long",
                DisplayName = recognizerId,
                LanguageCodes = { languageCode },
                DefaultRecognitionConfig = new RecognitionConfig {
                    Features = new RecognitionFeatures {
                        EnableAutomaticPunctuation = true,
                        MaxAlternatives = 0,
                        EnableSpokenPunctuation = false,
                        EnableSpokenEmojis = false,
                        ProfanityFilter = true,
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

    private string GetLanguageCode(Language language)
        => language.Code.Value switch {
            "EN" => "en-US",
            "ES" => "es-US",
            "FR" => "fr-FR",
            _ => language,
        };

    private static string FixSuffix(string prefix, string suffix)
    {
        var firstLetterIndex = Transcript.ContentStartRe.Match(suffix).Length;
        if (firstLetterIndex == suffix.Length)
            return suffix; // Suffix is all whitespace or empty

        if (firstLetterIndex == 0 && !EndsWithWhitespaceOrEmptyRe.IsMatch(prefix)) {
            // Add spacer
            suffix = " " + suffix;
            firstLetterIndex++;
        }
        else if (firstLetterIndex > 0 && EndsWithWhitespaceOrEmptyRe.IsMatch(prefix)) {
            // Remove spacer
            suffix = suffix[firstLetterIndex..];
            firstLetterIndex = 0;
        }

        if (CompleteSentenceOrEmptyRe.IsMatch(prefix))
            suffix = suffix.Capitalize(firstLetterIndex);

        return suffix;
    }

    private float? TryGetOriginalAudioTime(Duration? time)
        => time is { } vTime ? GetOriginalAudioTime(vTime) : null;
    private float? TryGetOriginalAudioTime(float? time)
        => time is { } vTime ? GetOriginalAudioTime(vTime) : null;
    private float GetOriginalAudioTime(Duration time)
        => GetOriginalAudioTime((float)time.ToTimeSpan().TotalSeconds);
    private float GetOriginalAudioTime(float time)
        => (float)Math.Round(Math.Max(0, time - SilentPrefixDuration.TotalSeconds), 2);

    private AudioSource AddSilentPrefixAndSuffix(AudioSource audioSource, CancellationToken cancellationToken)
        => SilenceAudioSource
            .Take(SilentPrefixDuration, cancellationToken)
            .Concat(audioSource, cancellationToken)
            .ConcatUntil(SilenceAudioSource, SilentSuffixDuration, cancellationToken);

    private static Task<AudioSource> LoadSilenceAudio()
    {
        var byteStream = typeof(GoogleTranscriber).Assembly
            .GetManifestResourceStream("ActualChat.Transcription.data.silence.opuss")!
            .ReadByteStream(true);
        var converter = new ActualOpusStreamConverter(MomentClockSet.Default, DefaultLog);
        return converter.FromByteStream(byteStream, CancellationToken.None);
    }
}
