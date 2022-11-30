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
    private static readonly Regex _invalidCharsRe = new ("[^a-z0-9-]",RegexOptions.Compiled | RegexOptions.CultureInvariant);
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
        TranscriptionOptions options,
        AudioSource audioSource,
        CancellationToken cancellationToken)
    {
        var prefix = _invalidCharsRe.Replace(transcriberKey.Value.ToLowerInvariant().Truncate(40), "-");
        var recognizerId = $"r-{prefix}-{options.Language.ToLowerInvariant()}";
        var recognizerTask = GetOrCreateRecognizer(recognizerId, options, cancellationToken);
        var process = new GoogleTranscriberProcess(recognizerTask, options, audioSource, Log);
        process.Run().ContinueWith(_ => process.DisposeAsync(), TaskScheduler.Default);
        return process.GetTranscripts(cancellationToken);

        async Task<Recognizer> GetOrCreateRecognizer(string recognizerId1, TranscriptionOptions options1, CancellationToken cancellationToken1)
        {
            var recognizer = await Cache.GetOrCreateAsync(recognizerId1,
            async entry => {
                var speechClient = await new SpeechClientBuilder().BuildAsync(cancellationToken1).ConfigureAwait(false);
                var projectId = await _projectId.Value.ConfigureAwait(false);

                var parent = $"projects/{projectId}/locations/global";
                var recognizerName = $"{parent}/recognizers/{recognizerId}";
                try {
                    var existingRecognizer = await speechClient.GetRecognizerAsync(
                        new GetRecognizerRequest {
                            Name = recognizerName,
                        },
                        cancellationToken1).ConfigureAwait(false);
                    if (existingRecognizer.ExpireTime != null)
                        entry.AbsoluteExpiration = existingRecognizer.ExpireTime.ToDateTimeOffset().AddSeconds(-10);
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
                                    MaxAlternatives = 1,
                                    DiarizationConfig = new SpeakerDiarizationConfig {
                                        MinSpeakerCount = 1,
                                        MaxSpeakerCount = options1.MaxSpeakerCount ?? 1,
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
                    },
                    // CallSettings.FromCancellationToken(cancellationToken));
                    new CallSettings(cancellationToken1, Expiration.FromTimeout(TimeSpan.FromMinutes(30)), null, null, WriteOptions.Default, null))
                    .ConfigureAwait(false);

                var completedNewRecognizerOperation = await newRecognizerOperation.PollUntilCompletedAsync().ConfigureAwait(false);
                var newRecognizer = completedNewRecognizerOperation.Result;
                if (newRecognizer.ExpireTime != null)
                    entry.AbsoluteExpiration = newRecognizer.ExpireTime.ToDateTimeOffset().AddSeconds(-10);
                // let's wait for some time while the recognizer become operational
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken1).ConfigureAwait(false);
                return newRecognizer!;
            });

            return recognizer!;
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
}
