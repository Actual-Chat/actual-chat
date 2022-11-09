using ActualChat.Audio;
using ActualChat.Module;
using Google.Api.Gax;
using Google.Cloud.Speech.V2;
using Microsoft.Extensions.Caching.Memory;

namespace ActualChat.Transcription.Google;

public class GoogleTranscriber : ITranscriber
{
    private readonly Lazy<Task<Location>> _location;

    private CoreSettings CoreSettings { get; }
    private TranscriptionOptions Options { get; }
    private IMemoryCache Cache { get; }
    private ILogger Log { get; }

    public GoogleTranscriber(
        CoreSettings coreSettings,
        TranscriptionOptions options,
        IMemoryCache cache,
        ILogger<GoogleTranscriber>? log = null)
    {
        CoreSettings = coreSettings;
        Options = options;
        Cache = cache;
        Log = log ?? NullLogger<GoogleTranscriber>.Instance;
        #pragma warning disable VSTHRD011
        _location = new Lazy<Task<Location>>(BackgroundTask.Run(LoadLocation));
    }

    public IAsyncEnumerable<Transcript> Transcribe(
        Symbol transcriberKey,
        TranscriptionOptions options,
        AudioSource audioSource,
        CancellationToken cancellationToken)
    {
        var recognizerId = transcriberKey.Value;
        var recognizerTask = GetOrCreateRecognizer(recognizerId, cancellationToken);
        var process = new GoogleTranscriberProcess(recognizerTask, options, audioSource, Log);
        process.Run().ContinueWith(_ => process.DisposeAsync(), TaskScheduler.Default);
        return process.GetTranscripts(cancellationToken);

        async Task<Recognizer> GetOrCreateRecognizer(string recognizerId1, CancellationToken cancellationToken1)
        {
            var recognizer = await Cache.GetOrCreateAsync(recognizerId1,
            async entry => {
                var speechClient = await new SpeechClientBuilder().BuildAsync(cancellationToken1).ConfigureAwait(false);
                var location = await _location.Value;

                var parent = $"projects/{location.ProjectId}/locations/{location.RegionId}";
                var recognizerName = $"{parent}/recognizers/{recognizerId}";
                var existingRecognizer = await speechClient.GetRecognizerAsync(
                    new GetRecognizerRequest {
                        Name = recognizerName,
                    },
                    cancellationToken1);
                var newRecognizerOperation = await speechClient.CreateRecognizerAsync(
                    new CreateRecognizerRequest {
                        Parent = parent,
                        RecognizerId = recognizerId,
                        Recognizer = new Recognizer {
                            Model = "latest_long",
                            DisplayName = recognizerName,
                            LanguageCodes = { options.Language },
                            DefaultRecognitionConfig = new RecognitionConfig {
                                Features = new RecognitionFeatures {
                                    EnableAutomaticPunctuation = true,
                                    MaxAlternatives = 1,
                                    DiarizationConfig = new SpeakerDiarizationConfig {
                                        MinSpeakerCount = 1,
                                        MaxSpeakerCount = Options.MaxSpeakerCount ?? 5,
                                    },
                                    EnableSpokenPunctuation = true,
                                    EnableSpokenEmojis = true,
                                    ProfanityFilter = false,
                                    EnableWordConfidence = true,
                                    EnableWordTimeOffsets = true,
                                    MultiChannelMode = RecognitionFeatures.Types.MultiChannelMode.Unspecified,
                                },
                                AutoDecodingConfig = new AutoDetectDecodingConfig(),
                            },
                        },
                    }, cancellationToken1);

                var completedNewRecognizerOperation = await newRecognizerOperation.PollUntilCompletedAsync();
                var newRecognizer = completedNewRecognizerOperation.Result;
                entry.AbsoluteExpiration = newRecognizer.ExpireTime.ToDateTimeOffset().AddSeconds(-10);
                return newRecognizer;
            });

            return recognizer;
        }
    }

    private async Task<Location> LoadLocation()
    {
        if (!CoreSettings.GoogleProjectId.IsNullOrEmpty() && !CoreSettings.GoogleRegionId.IsNullOrEmpty())
            return new Location(CoreSettings.GoogleProjectId, CoreSettings.GoogleRegionId);

        var platform = await Platform.InstanceAsync().ConfigureAwait(false);
        return new Location(platform.ProjectId, platform.GkeDetails.Location);
    }

    private record Location(string ProjectId, string RegionId);
}
