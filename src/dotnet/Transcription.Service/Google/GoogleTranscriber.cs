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
        Log.LogDebug("Getting recognizer");
        var (lang, region) = GetLocalLanguageAndRegion(options.Language, CoreSettings.GoogleRegionId);
        var recognizerId = $"{lang.ToLowerInvariant()}";
        var recognizerTask = GetOrCreateRecognizer(recognizerId, options, cancellationToken);
        var process = new GoogleTranscriberProcess(recognizerTask, streamId, audioSource, options, Log);
        process.Run().ContinueWith(_ => process.DisposeAsync(), TaskScheduler.Default);
        return process.GetTranscripts(cancellationToken);

        async Task<string> GetOrCreateRecognizer(string recognizerId1, TranscriptionOptions options1, CancellationToken cancellationToken1)
        {
            var recognizer = await Cache.GetOrCreateAsync(recognizerId1,
            async _ => {
                var speechClient = await new SpeechClientBuilder().BuildAsync(cancellationToken1).ConfigureAwait(false);
                var projectId = await _projectId.Value.ConfigureAwait(false);

                var parent = $"projects/{projectId}/locations/{region}";
                var recognizerName = $"{parent}/recognizers/{recognizerId}";
                try {
                    var existingRecognizer = await speechClient.GetRecognizerAsync(
                        new GetRecognizerRequest {
                            Name = recognizerName,
                        },
                        cancellationToken1).ConfigureAwait(false);
                    if (existingRecognizer.State == Recognizer.Types.State.Active)
                        return existingRecognizer;
                }
                catch (RpcException e) when (e.StatusCode is StatusCode.NotFound) { }

                var newRecognizerOperation = await speechClient.CreateRecognizerAsync(
                    new CreateRecognizerRequest {
                        Parent = parent,
                        RecognizerId = recognizerId,
                        Recognizer = new Recognizer {
                            Model = "latest_long",
                            DisplayName = recognizerId,
                            LanguageCodes = { options.Language },
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
                    },
                    CallSettings.FromCancellationToken(cancellationToken))
                .ConfigureAwait(false);

                var completedNewRecognizerOperation = await newRecognizerOperation.PollUntilCompletedAsync().ConfigureAwait(false);
                var newRecognizer = completedNewRecognizerOperation.Result;
                return newRecognizer;
            }).ConfigureAwait(false);

            return recognizer!.Name;
        }
    }

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

    private (string Code, string Region) GetLocalLanguageAndRegion(LanguageId languageId, string regionId)
        => (languageId.Code, regionId) switch {
           ("EN", "us-central1") => ("en-US", "us-central1"),
           ("ES", "us-central1") => ("es-US", "us-central1"),
           ("FR", "us-central1") => ("fr-CA", "northamerica-northeast1"),
           (_, _) => (languageId.Value, "global"),
        };
}
