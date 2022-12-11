using ActualChat.Audio;
using ActualChat.Module;
using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V2;
using Grpc.Core;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Transcription.Google;

public class GoogleTranscriber : ITranscriber
{
    private readonly CancellationToken _stopToken;
    private readonly IThreadSafeLruCache<string, Task<string>> _cache;
    private readonly Task<string> _projectIdTask;

    private CoreSettings CoreSettings { get; }
    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.TranscriberGoogle || Constants.DebugMode.TranscriberAny;

    public GoogleTranscriber(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        CoreSettings = services.GetRequiredService<CoreSettings>();
        var hostApplicationLifetime = services.GetService<IHostApplicationLifetime>();

        _stopToken = hostApplicationLifetime?.ApplicationStopping ?? CancellationToken.None;
        _cache = new ThreadSafeLruCache<string, Task<string>>(10);
        _projectIdTask = BackgroundTask.Run(LoadProjectId);
    }

    public IAsyncEnumerable<Transcript> Transcribe(
        Symbol transcriberKey,
        string streamId,
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken)
    {
        var recognizerTask = GetRecognizer(options, cancellationToken);
        var process = new GoogleTranscriberProcess(recognizerTask, streamId, audioSource, options, Log);
        process.Run().ContinueWith(_ => process.DisposeAsync(), TaskScheduler.Default);
        return process.GetTranscripts(cancellationToken);
    }

    // Private methods

    private Task<string> GetRecognizer(TranscriptionOptions options, CancellationToken cancellationToken)
    {
        var region = GetRegionId();
        var languageCode = GetLanguageCode(options.Language);
        var recognizerId = $"{languageCode.ToLowerInvariant()}";

        var recognizerTask = _cache.GetOrCreate(recognizerId, async _ => {
            var speechClient = await new SpeechClientBuilder().BuildAsync(_stopToken).ConfigureAwait(false);
            var projectId = await GetProjectId().ConfigureAwait(false);

            var parent = $"projects/{projectId}/locations/{region}";
            var name = $"{parent}/recognizers/{recognizerId}";

            retry:
            try {
                var getRecognizerRequest = new GetRecognizerRequest { Name = name };
                var existingRecognizer = await speechClient
                    .GetRecognizerAsync(getRecognizerRequest, _stopToken)
                    .ConfigureAwait(false);
                if (existingRecognizer.State == Recognizer.Types.State.Active)
                    return existingRecognizer.Name;
            }
            catch (RpcException e) when (
                e.StatusCode is StatusCode.InvalidArgument
                && e.Status.Detail.OrdinalStartsWith("Expected resource location to be global"))
            {
                parent = $"projects/{projectId}/locations/global";
                name = $"{parent}/recognizers/{recognizerId}";
                goto retry;
            }
            catch (RpcException e) when (e.StatusCode is StatusCode.NotFound) {
                // NOTE(AY): Intended, it's created further in this case
            }

            DebugLog?.LogDebug("Creating new recognizer: Id = {RecognizerName}", recognizerId);
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
            var createRecognizerOperation = await speechClient
                .CreateRecognizerAsync(createRecognizerRequest, _stopToken)
                .ConfigureAwait(false);
            var createRecognizerCompleted = await createRecognizerOperation
                .PollUntilCompletedAsync(null, CallSettings.FromCancellationToken(_stopToken))
                .ConfigureAwait(false);
            var newRecognizer = createRecognizerCompleted.Result;
            return newRecognizer.Name;
        });

        if (recognizerTask.IsFaulted && !recognizerTask.IsCanceled)
            _cache.Remove(recognizerId); // We'll retry on the next attempt to get it

        return recognizerTask.WaitAsync(cancellationToken);
    }

    private string GetRegionId()
        => CoreSettings.GoogleRegionId.NullIfEmpty()
            ?? throw StandardError.Configuration($"{nameof(CoreSettings)}.{nameof(CoreSettings.GoogleRegionId)} is not set.");

    private string GetLanguageCode(Language language)
        => language.Code.Value switch {
            "ES" => "es-US",
            "FR" => "fr-CA",
            _ => language.Value,
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
}
