using System.Numerics;
using ActualChat.Audio;
using ActualChat.Module;
using Cysharp.Text;
using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V2;
using Google.Protobuf;
using Grpc.Core;

namespace ActualChat.Transcription.Google;

public class GoogleTranscriber : ITranscriber
{
    private static readonly string RegionId = "us";
    private static readonly TimeSpan SilentPrefixDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SilentSuffixDuration = TimeSpan.FromSeconds(4);

    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.TranscriberGoogle || Constants.DebugMode.TranscriberAny;

    private IServiceProvider Services { get; }
    private CoreSettings CoreSettings { get; }
    private MomentClockSet Clocks { get; }

    private Task WhenInitialized { get; }
    private SpeechClient SpeechClient { get; set; } = null!; // Post-WhenInitialized
    private StreamingRecognitionConfig RecognitionConfig { get; set; } = null!; // Post-WhenInitialized
    private string GoogleProjectId { get; set; } = null!; // Post-WhenInitialized
    private AudioSource SilenceAudioSource { get; set; } = null!; // Post-WhenInitialized

    public GoogleTranscriber(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Services = services;
        CoreSettings = services.GetRequiredService<CoreSettings>();
        Clocks = services.GetRequiredService<MomentClockSet>();
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
        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);

        var languageCode = GetLanguageCode(options.Language);
        var recognizerId = $"{languageCode.ToLowerInvariant()}";
        var parent = $"projects/{GoogleProjectId}/locations/{RegionId}";
        var recognizerName = $"{parent}/recognizers/{recognizerId}";

        var converter = new WebMStreamConverter(Clocks, Log);
        var resultAudioSource = AugmentSourceAudio(audioSource, cancellationToken);
        var byteStream = converter
            .ToByteStream(resultAudioSource, cancellationToken)
            .Memoize(cancellationToken);

        Log.LogDebug("Starting recognize process for {AudioStreamId}", audioStreamId);
        try {
            await Transcribe(recognizerName, byteStream.Replay(cancellationToken), output, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RpcException e) when (
            e.StatusCode is StatusCode.NotFound
            && e.Status.Detail.OrdinalStartsWith("Unable to find Recognizer"))
        {
            await CreateRecognizer(recognizerId, parent, options, cancellationToken)
                .ConfigureAwait(false);
            await Transcribe(recognizerName, byteStream.Replay(cancellationToken), output, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // Private methods

    private async Task Transcribe(
        string recognizerName,
        IAsyncEnumerable<byte[]> audioSource,
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

        // We want to stop both tasks here on any failure, so...
        var cts = cancellationToken.CreateLinkedTokenSource();
        try {
            var pushAudioTask = PushAudio(audioSource, recognizeStream, cts.Token);
            var pullResponsesTask = PullResponses(recognizeStream, output, cts.Token);
            await Task.WhenAll(pushAudioTask, pullResponsesTask).ConfigureAwait(false);
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }

    private async Task PushAudio(
        IAsyncEnumerable<byte[]> audioSource,
        SpeechClient.StreamingRecognizeStream recognizeStream,
        CancellationToken cancellationToken)
    {
        try {
            await foreach (var chunk in audioSource.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                var request = new StreamingRecognizeRequest {
                    StreamingConfig = RecognitionConfig,
                    Audio = ByteString.CopyFrom(chunk),
                };
                await recognizeStream.WriteAsync(request).ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            Log.LogError(e, $"{nameof(PushAudio)} failed");
            throw;
        }
        finally {
            await recognizeStream
                .WriteCompleteAsync()
                .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private async Task PullResponses(
        SpeechClient.StreamingRecognizeStream recognizeStream,
        ChannelWriter<Transcript> output,
        CancellationToken cancellationToken)
    {
        try {
            var responses = (IAsyncEnumerable<StreamingRecognizeResponse>)recognizeStream.GetResponseStream();
            await foreach (var transcript in ProcessResponses(responses).ConfigureAwait(false))
                await output.WriteAsync(transcript, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, $"{nameof(PullResponses)} failed");
            throw;
        }
    }

    // It's internal to be accessible from tests
    internal async IAsyncEnumerable<Transcript> ProcessResponses(
        IAsyncEnumerable<StreamingRecognizeResponse> responses)
    {
        var state = new GoogleTranscriberState();
        await foreach (var response in responses.ConfigureAwait(false)) {
            var transcript = ProcessResponse(state, response);
            if (transcript != null)
                yield return transcript;
        }
        yield return state.MarkStable();
    }

    private Transcript? ProcessResponse(
        GoogleTranscriberState state,
        StreamingRecognizeResponse response)
    {
        DebugLog?.LogDebug("Response={Response}", response);

        Transcript transcript;
        var results = response.Results;
        var hasFinal = results.Any(r => r.IsFinal);
        if (hasFinal) {
            var result = results.Single(r => r.IsFinal);
            if (!TryParseFinal(state, result, out var text, out var timeMap)) {
                Log.LogWarning("Final transcript discarded. State.LastStable={LastStable}, Response={Response}",
                    state.Stable, response);
                return null;
            }
            transcript = state.AppendStable(text, timeMap);
        }
        else {
            var text = results
                .Select(r => r.Alternatives.First().Transcript)
                .ToDelimitedString("");

            var resultEndOffset = results.First().ResultEndOffset;
            var endTime = resultEndOffset == null
                ? null
                : (float?) resultEndOffset.ToTimeSpan().TotalSeconds;

            // Google Transcribe issue: doesn't provide IsFinal results time to time, so let's implement some heuristics
            // when we can Complete current transcript
            if (ReferenceEquals(state.Stable, Transcript.Empty)) {
                if (state.Unstable.Length > text.Length + 4)
                    state.MarkStable();
            }
            else {
                var diffMap = state.Stable.TimeMap.GetDiffSuffix(state.Unstable.TimeMap);
                if (diffMap.XRange.Size() > text.Length + 24)
                    state.MarkStable();
            }

            if (state.Stable.Text.Length != 0 && !text.OrdinalStartsWith(" ")) {
                // Google Transcribe issue: sometimes it returns alternatives w/o " " prefix,
                // i.e. they go concatenated with the stable (final) part.
                text = ZString.Concat(" ", text);
            }

            transcript = state.AppendUnstable(text, endTime);
        }
        DebugLog?.LogDebug("Transcript={Transcript}", transcript);
        return transcript;
    }

    private bool TryParseFinal(
        GoogleTranscriberState state,
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
        DebugLog?.LogDebug("Creating new recognizer: #{RecognizerId}", recognizerId);
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
                        ProfanityFilter = false,
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
        DebugLog?.LogDebug("Creating new recognizer: #{RecognizerId} -> {Result}", recognizerId, result);
    }

    private string GetLanguageCode(Language language)
        => language.Code.Value switch {
            "EN" => "en-US",
            "ES" => "es-US",
            "FR" => "fr-FR",
            _ => language,
        };

    private float GetOriginalAudioTime(float time)
        => (float)Math.Round(Math.Max(0, time - SilentPrefixDuration.TotalSeconds), 2);

    private AudioSource AugmentSourceAudio(AudioSource audioSource, CancellationToken cancellationToken)
        => SilenceAudioSource
            .Take(SilentPrefixDuration, cancellationToken)
            .Concat(audioSource, cancellationToken)
            .ConcatUntil(SilenceAudioSource, TimeSpan.FromSeconds(4), cancellationToken);

    private static Task<AudioSource> LoadSilenceAudio()
    {
        var byteStream = typeof(GoogleTranscriber).Assembly
            .GetManifestResourceStream("ActualChat.Transcription.data.silence.opuss")!
            .ReadByteStream(true);
        var converter = new ActualOpusStreamConverter(MomentClockSet.Default, DefaultLog);
        return converter.FromByteStream(byteStream, CancellationToken.None);
    }
}
