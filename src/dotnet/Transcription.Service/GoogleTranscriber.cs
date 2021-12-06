using ActualChat.Audio;
using ActualChat.Media;
using Google.Cloud.Speech.V1P1Beta1;
using Google.Protobuf;

namespace ActualChat.Transcription;

public class GoogleTranscriber : ITranscriber
{
    private readonly ILogger<GoogleTranscriber> _log;

    public GoogleTranscriber(ILogger<GoogleTranscriber> log)
        => _log = log;

    public async IAsyncEnumerable<TranscriptUpdate> Transcribe(
        TranscriptionRequest request,
        IAsyncEnumerable<AudioStreamPart> audioStream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (streamId, format, options) = request;
        _log.LogInformation("Start transcription of stream #{StreamId}", (string)streamId);

        var builder = new SpeechClientBuilder();
        var speechClient = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
        var config = new RecognitionConfig {
            Encoding = MapEncoding(format.CodecKind),
            AudioChannelCount = format.ChannelCount,
            SampleRateHertz = format.SampleRate,
            LanguageCode = options.Language,
            EnableAutomaticPunctuation = options.IsPunctuationEnabled,
            DiarizationConfig = new () {
                EnableSpeakerDiarization = true,
                MaxSpeakerCount = options.MaxSpeakerCount ?? 5,
            },
        };

        var recognizeRequests = speechClient.StreamingRecognize();
        await recognizeRequests.WriteAsync(new () {
                StreamingConfig = new () {
                    Config = config,
                    InterimResults = true,
                    SingleUtterance = false,
                },
            }).ConfigureAwait(false);
        var recognizeResponses = recognizeRequests.GetResponseStream();

        var failureCts = new CancellationTokenSource();

        var sendAudioTask = Task.Run(async () => {
            try {
                await SendAudio(audioStream, recognizeRequests, cancellationToken).ConfigureAwait(false);
            }
            catch {
                failureCts.Cancel();
                throw;
            }
        }, CancellationToken.None);

        try {
            using var mutualCts = failureCts.Token.LinkWith(cancellationToken);
            var mutualToken = mutualCts.Token;
            var transcriptStream = ReadTranscript(recognizeResponses, mutualToken);
            await foreach (var update in transcriptStream.WithCancellation(mutualToken).ConfigureAwait(false))
                yield return update;
        }
        finally {
            if (failureCts.IsCancellationRequested)
                await sendAudioTask.ConfigureAwait(false);
            failureCts.CancelAndDisposeSilently();
        }
    }

    private async Task SendAudio(
        IAsyncEnumerable<AudioStreamPart> audioStream,
        SpeechClient.StreamingRecognizeStream recognizeRequests,
        CancellationToken cancellationToken)
    {
        try {
            await foreach (var part in audioStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                var request = new StreamingRecognizeRequest {
                    AudioContent = ByteString.CopyFrom(part.ToBlobPart().Data),
                };
                await recognizeRequests.WriteAsync(request).ConfigureAwait(false);
            }
        }
        finally {
            await recognizeRequests.WriteCompleteAsync().ConfigureAwait(false);
        }
    }

    internal async IAsyncEnumerable<TranscriptUpdate> ReadTranscript(
        IAsyncEnumerable<StreamingRecognizeResponse> recognizeResponses,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var updateExtractor = new TranscriptUpdateExtractor();
        await foreach (var response in recognizeResponses.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            ProcessResponse(response);
            while (updateExtractor.Updates.TryDequeue(out var update))
                yield return update;
        }
        updateExtractor.Complete();
        while (updateExtractor.Updates.TryDequeue(out var update))
            yield return update;

        void ProcessResponse(StreamingRecognizeResponse response)
        {
            _log.LogTrace("{Response}", response);
            if (response.Error != null) {
                _log.LogError("Transcription error: Code: {ErrorCode}, Message: {ErrorMessage}",
                    response.Error.Code,
                    response.Error.Message);
                throw new TranscriptionException(
                    response.Error.Code.ToString(CultureInfo.InvariantCulture),
                    response.Error.Message);
            }

            foreach (var result in response.Results) {
                var alternative = result.Alternatives.First();
                if (result.Stability < 0.02 && !result.IsFinal)
                    continue;

                var endTime = result.ResultEndTime.ToTimeSpan().TotalSeconds;
                var text = alternative.Transcript;
                var finalizedPart = updateExtractor.FinalizedPart;
                var finalizedTextLength = finalizedPart.TextToTimeMap.SourceRange.Max;
                var finalizedSpeechDuration = finalizedPart.TextToTimeMap.TargetRange.Max;
                if (result.IsFinal) {
                    if (Math.Abs(finalizedSpeechDuration - endTime) < 0.00001d)
                        break; // we have already processed final results up to endTime

                    var sourcePoints = new List<double> { finalizedTextLength };
                    var targetPoints = new List<double> { finalizedSpeechDuration };
                    var textIndex = 0;
                    foreach (var word in alternative.Words.SkipWhile(w => w.StartTime.ToTimeSpan().Seconds < finalizedSpeechDuration)) {
                        var wordIndex = text.IndexOf(word.Word, textIndex, StringComparison.InvariantCultureIgnoreCase);
                        if (wordIndex < 0)
                            continue;

                        textIndex = wordIndex + word.Word.Length;
                        if (!(sourcePoints[^1] < finalizedTextLength + wordIndex))
                            continue;

                        sourcePoints.Add(finalizedTextLength + wordIndex);
                        targetPoints.Add(word.StartTime.ToTimeSpan().TotalSeconds);
                    }
                    sourcePoints.Add(finalizedTextLength + text.Length);
                    targetPoints.Add(endTime);
                    var currentPart = sourcePoints.Count > 2
                        ? new Transcript { Text = text, TextToTimeMap = new (sourcePoints.ToArray(), targetPoints.ToArray())}
                        : new Transcript { Text = text, TextToTimeMap = new (
                            new[] { (double)finalizedTextLength, finalizedTextLength + text.Length },
                            new[] { finalizedSpeechDuration, endTime })};

                    updateExtractor.FinalizeWith(currentPart);
                }
                else {
                    updateExtractor.EnqueueUpdate(text, endTime);
                    break; // We process only the first one of non-final results
                }
            }
        }
    }

    private static RecognitionConfig.Types.AudioEncoding MapEncoding(AudioCodecKind codecKind)
    {
        switch (codecKind) {
        case AudioCodecKind.Wav:
            return RecognitionConfig.Types.AudioEncoding.Linear16;
        case AudioCodecKind.Flac:
            return RecognitionConfig.Types.AudioEncoding.Flac;
        case AudioCodecKind.Opus:
            return RecognitionConfig.Types.AudioEncoding.WebmOpus;
        default:
            return RecognitionConfig.Types.AudioEncoding.EncodingUnspecified;
        }
    }
}
