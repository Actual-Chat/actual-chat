using System.Text.RegularExpressions;
using ActualChat.Audio;
using ActualChat.Module;
using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V2;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;

namespace ActualChat.Transcription.Google;

public class GoogleTranscriber : ITranscriber
{
    private readonly Lazy<Task<string>> _projectId;

    private CoreSettings CoreSettings { get; }
    private IMemoryCache Cache { get; }
    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.TranscriberGoogle || Constants.DebugMode.TranscriberAny;

    public GoogleTranscriber(
        CoreSettings coreSettings,
        IMemoryCache cache,
        ILogger<GoogleTranscriber>? log = null)
    {
        CoreSettings = coreSettings;
        Cache = cache;
        Log = log ?? NullLogger<GoogleTranscriber>.Instance;
        #pragma warning disable VSTHRD011
        _projectId = new Lazy<Task<string>>(BackgroundTask.Run(LoadProjectId));
    }

    public IAsyncEnumerable<Transcript> Transcribe(
        Symbol transcriberKey,
        string streamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken)
    {
        var getRecognizerIdTask = GetRecognizerId(options, cancellationToken);
        var process = new GoogleTranscriberProcess(getRecognizerIdTask, streamId, audioSource, options, Log);
        process.Run().ContinueWith(_ => process.DisposeAsync(), TaskScheduler.Default);
        return process.GetTranscripts(cancellationToken);
    }

    // Private methods

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

    private async Task<string> GetRecognizerId(TranscriptionOptions options, CancellationToken cancellationToken)
    {
        var (languageCode, region) = GetLanguageCodeAndRegion(options.Language, CoreSettings.GoogleRegionId);
        var recognizerId = $"{languageCode.ToLowerInvariant()}";

        var recognizer = await Cache.GetOrCreateAsync(recognizerId, async _ => {
            var speechClient = await new SpeechClientBuilder().BuildAsync(cancellationToken).ConfigureAwait(false);
            var projectId = await _projectId.Value.ConfigureAwait(false);

            var parent = $"projects/{projectId}/locations/{region}";
            var recognizerName = $"{parent}/recognizers/{recognizerId}";
            try {
                var getRecognizerRequest = new GetRecognizerRequest { Name = recognizerName };
                var existingRecognizer = await speechClient
                    .GetRecognizerAsync(getRecognizerRequest, cancellationToken)
                    .ConfigureAwait(false);
                if (existingRecognizer.State == Recognizer.Types.State.Active)
                    return existingRecognizer;
            }
            catch (RpcException e) when (e.StatusCode is StatusCode.NotFound) {
                // NOTE(AY): Intended, it's created further in this case
            }

            DebugLog?.LogDebug("Creating new recognizer, Id = {RecognizerId}", recognizerId);
            var createRecognizerRequest = new CreateRecognizerRequest {
                Parent = parent,
                RecognizerId = recognizerId,
                Recognizer = new Recognizer {
                    Model = "latest_long",
                    DisplayName = recognizerId,
                    LanguageCodes = { options.Language.Value },
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
            var createRecognizer = await speechClient
                .CreateRecognizerAsync(createRecognizerRequest, CallSettings.FromCancellationToken(cancellationToken))
                .ConfigureAwait(false);
            var createRecognizerCompleted = await createRecognizer
                .PollUntilCompletedAsync()
                .ConfigureAwait(false);
            var newRecognizer = createRecognizerCompleted.Result;
            return newRecognizer;
        }).ConfigureAwait(false);

        return recognizer!.Name;
    }

    private (string Code, string Region) GetLanguageCodeAndRegion(Language language, string regionId)
        => (language.Code.Value, regionId) switch {
           ("EN", "us-central1") => ("en-US", "us-central1"),
           ("ES", "us-central1") => ("es-US", "us-central1"),
           ("FR", "us-central1") => ("fr-CA", "northamerica-northeast1"),
           (_, _) => (language.Value, "global"),
        };
}
