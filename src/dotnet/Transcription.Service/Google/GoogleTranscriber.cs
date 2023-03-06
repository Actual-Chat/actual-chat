using ActualChat.Audio;
using ActualChat.Module;
using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V2;
using Grpc.Core;

namespace ActualChat.Transcription.Google;

public class GoogleTranscriber : ITranscriber
{
    private static readonly Task<AudioSource> _silenceAudioSourceTask = BackgroundTask.Run(LoadSilenceAudio);

    private readonly Task<string> _projectIdTask;

    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.TranscriberGoogle || Constants.DebugMode.TranscriberAny;

    private CoreSettings CoreSettings { get; }
    private MomentClockSet Clocks { get; }

    public GoogleTranscriber(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        CoreSettings = services.GetRequiredService<CoreSettings>();
        Clocks = services.GetRequiredService<MomentClockSet>();
        _projectIdTask = BackgroundTask.Run(LoadProjectId);
    }

    public async IAsyncEnumerable<Transcript> Transcribe(
        Symbol transcriberKey,
        string streamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var region = GetRegionId(options.Language);
        var languageCode = GetLanguageCode(options.Language);
        var projectId = await GetProjectId().ConfigureAwait(false);
        var endpoint = $"{region}-speech.googleapis.com:443";
        var recognizerId = $"{languageCode.ToLowerInvariant()}";
        var parent = $"projects/{projectId}/locations/{region}";
        var recognizerName = $"{parent}/recognizers/{recognizerId}";
        var builder = new SpeechClientBuilder {
            Endpoint = endpoint,
        };
        var speechClient = await builder.BuildAsync(CancellationToken.None).ConfigureAwait(false);
        var recognizeRequests = speechClient
            .StreamingRecognize(
                CallSettings.FromCancellationToken(CancellationToken.None),
                new BidirectionalStreamingSettings(1));
        var streamingRecognitionConfig = new StreamingRecognitionConfig {
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
        Log.LogDebug("Starting recognize process for {StreamId}", streamId);
        await recognizeRequests.WriteAsync(new StreamingRecognizeRequest {
            StreamingConfig = streamingRecognitionConfig,
            Recognizer = recognizerName,
        }).ConfigureAwait(false);

        var webMStreamAdapter = new WebMStreamAdapter(Clocks, Log);
        var silenceAudioSource = await _silenceAudioSourceTask.ConfigureAwait(false);
        var resultAudioSource = silenceAudioSource
            .Take(TimeSpan.FromMilliseconds(2000), cancellationToken)
            .Concat(audioSource, cancellationToken)
            .ConcatUntil(silenceAudioSource, TimeSpan.FromSeconds(4), cancellationToken);
        var byteStream = webMStreamAdapter.Write(resultAudioSource, cancellationToken);
        var memoizedByteStream = byteStream.Memoize(cancellationToken);
        var transcriptChannel = Channel.CreateUnbounded<Transcript>(new UnboundedChannelOptions {
            SingleWriter = true,
            SingleReader = true,
        });

        var handleTranscriptCts = cancellationToken.CreateLinkedTokenSource();
        var runTask = BackgroundTask.Run(
            () => HandleTranscription(cancellationToken),
            Log,
            $"{nameof(GoogleTranscriber)}.{nameof(Transcribe)} failed",
            cancellationToken);

        await foreach(var transcript in transcriptChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return transcript;

        await runTask.ConfigureAwait(false);

        async Task HandleTranscription(CancellationToken cancellationToken1)
        {
            Exception? error = null;
            retry:
            var needsFinally = true;
            try {
                var process = new GoogleTranscriberProcess(
                    memoizedByteStream.Replay(handleTranscriptCts.Token),
                    recognizeRequests,
                    streamingRecognitionConfig,
                    Clocks,
                    Log);
                var processRunTask = process.Run();
                await using var _ = process.ConfigureAwait(false);
                await foreach (var transcript in process.GetTranscripts(handleTranscriptCts.Token).ConfigureAwait(false))
                    await transcriptChannel.Writer.WriteAsync(transcript, handleTranscriptCts.Token).ConfigureAwait(false);
                await processRunTask.ConfigureAwait(false);
            }
            catch (RpcException e) when (
                e.StatusCode is StatusCode.NotFound
                && e.Status.Detail.OrdinalStartsWith("Unable to find Recognizer")) {

                handleTranscriptCts.CancelAndDisposeSilently();

                await CreateRecognizer(speechClient,
                        recognizerId,
                        parent,
                        options,
                        cancellationToken1)
                    .ConfigureAwait(false);

                needsFinally = false;
                Log.LogWarning("Restarting recognize process for {StreamId}", streamId);
                recognizeRequests = speechClient
                    .StreamingRecognize(
                        CallSettings.FromCancellationToken(cancellationToken1),
                        new BidirectionalStreamingSettings(1));
                await recognizeRequests.WriteAsync(new StreamingRecognizeRequest {
                    StreamingConfig = streamingRecognitionConfig,
                    Recognizer = recognizerName,
                }).ConfigureAwait(false);

                handleTranscriptCts = cancellationToken1.CreateLinkedTokenSource();

                goto retry;
            }
            catch (Exception e) {
                error = e;
                throw;
            }
            finally {
                if (needsFinally)
                    transcriptChannel.Writer.TryComplete(error);
            }
        }
    }

    // Private methods

    private async Task<string> CreateRecognizer(
        SpeechClient speechClient,
        string recognizerId,
        string recognizerParent,
        TranscriptionOptions options,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("Creating new recognizer: Id = {RecognizerName}", recognizerId);
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
        var createRecognizerOperation = await speechClient
            .CreateRecognizerAsync(createRecognizerRequest, cancellationToken)
            .ConfigureAwait(false);
        var createRecognizerCompleted = await createRecognizerOperation
            .PollUntilCompletedAsync(null, CallSettings.FromCancellationToken(cancellationToken))
            .ConfigureAwait(false);
        var newRecognizer = createRecognizerCompleted.Result;
        return newRecognizer.Name;
    }

    // https://cloud.google.com/speech-to-text/v2/docs/speech-to-text-supported-languages
    // See supported languages
    private string GetRegionId(Language language)
    {
        var regionId = CoreSettings.GoogleRegionId.NullIfEmpty()
            ?? throw StandardError.Configuration(
                $"{nameof(CoreSettings)}.{nameof(CoreSettings.GoogleRegionId)} is not set.");
        return (regionId, language.Code.Value) switch {
            // region-specific recognizer endpoints are slow @ Jan 15, 2023
            // ("us-central1", "EN") => "us-central1",
            // ("us-central1", "ES") => "us-central1",
            _ => "us",
        };
    }

    private string GetLanguageCode(Language language)
        => language.Code.Value switch {
            "EN" => "en-US",
            "ES" => "es-US",
            "FR" => "fr-FR",
            _ => language,
        };

    private Task<string> GetProjectId()
        => _projectIdTask;

    private async Task<string> LoadProjectId()
    {
        if (!CoreSettings.GoogleProjectId.IsNullOrEmpty())
            return CoreSettings.GoogleProjectId;

        var platform = await Platform.InstanceAsync().ConfigureAwait(false);
        if (platform?.ProjectId == null)
            throw StandardError.NotSupported<GoogleTranscriber>(
                $"Requires GKE or explicit settings of {nameof(CoreSettings)}.{nameof(CoreSettings.GoogleProjectId)}");
        return platform.ProjectId;
    }

    private static Task<AudioSource> LoadSilenceAudio()
    {
        var byteStream = typeof(GoogleTranscriberProcess).Assembly
            .GetManifestResourceStream("ActualChat.Transcription.data.silence.opuss")!
            .ReadByteStream(true);
        var streamAdapter = new ActualOpusStreamAdapter(MomentClockSet.Default, DefaultLog);
        return streamAdapter.Read(byteStream, CancellationToken.None);
    }
}
