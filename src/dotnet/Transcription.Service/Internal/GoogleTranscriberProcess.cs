using ActualChat.Audio;
using ActualChat.Media;
using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V1P1Beta1;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.Transcription.Internal;

public class GoogleTranscriberProcess : AsyncProcessBase
{
    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.GoogleTranscriber;

    public TranscriptionOptions Options { get; }
    public IAsyncEnumerable<AudioStreamPart> AudioStream { get; }
    public Channel<TranscriptUpdate> Updates { get; }

    public GoogleTranscriberProcess(
        TranscriptionOptions options,
        IAsyncEnumerable<AudioStreamPart> audioStream,
        Channel<TranscriptUpdate>? updates = null,
        ILogger? log = null)
    {
        Log = log ?? NullLogger.Instance;
        Options = options;
        AudioStream = audioStream;
        Updates = updates ?? Channel.CreateBounded<TranscriptUpdate>(new BoundedChannelOptions(128) {
            SingleWriter = true,
            SingleReader = true,
        });
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var (format, frames) = await AudioStream.ToFormatAndFrames(cancellationToken).ConfigureAwait(false);
        await using var __ = frames.ConfigureAwait(false);

        var builder = new SpeechClientBuilder();
        var speechClient = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
        var config = new RecognitionConfig {
            Encoding = MapEncoding(format.CodecKind),
            AudioChannelCount = format.ChannelCount,
            SampleRateHertz = format.SampleRate,
            LanguageCode = Options.Language,
            EnableAutomaticPunctuation = Options.IsPunctuationEnabled,
            DiarizationConfig = new () {
                EnableSpeakerDiarization = true,
                MaxSpeakerCount = Options.MaxSpeakerCount ?? 5,
            },
        };

        var recognizeRequests = speechClient.StreamingRecognize(CallSettings.FromCancellationToken(cancellationToken));
        await recognizeRequests.WriteAsync(new () {
                StreamingConfig = new () {
                    Config = config,
                    InterimResults = true,
                    SingleUtterance = false,
                },
            }).ConfigureAwait(false);
        var recognizeResponses = (IAsyncEnumerable<StreamingRecognizeResponse>) recognizeRequests.GetResponseStream();

        var audioStream = Audio.AudioStream.New(format, frames.AsEnumerableOnce(true), cancellationToken);
        _ = PushAudio(audioStream, recognizeRequests, cancellationToken);

        await ProcessResponses(recognizeResponses, cancellationToken).ConfigureAwait(false);
    }

    internal async Task ProcessResponses(IAsyncEnumerable<StreamingRecognizeResponse> recognizeResponses, CancellationToken cancellationToken)
    {
        var updateExtractor = new TranscriptUpdateExtractor();
        try {
            await foreach (var response in recognizeResponses.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                DebugLog?.LogDebug("Google Transcribe response: {Response}", response);
                ProcessResponse(updateExtractor, response);
                while (updateExtractor.Updates.TryDequeue(out var update))
                    await Updates.Writer.WriteAsync(update, cancellationToken).ConfigureAwait(false);
            }

            updateExtractor.Complete();
            while (updateExtractor.Updates.TryDequeue(out var update))
                await Updates.Writer.WriteAsync(update, cancellationToken).ConfigureAwait(false);
            Updates.Writer.Complete();
        }
        catch (Exception e) {
            Updates.Writer.TryComplete(e);
            throw;
        }
    }

    private void ProcessResponse(TranscriptUpdateExtractor updateExtractor, StreamingRecognizeResponse response)
    {
        Log.LogDebug("{Response}", response);
        if (response.Error != null) {
            Log.LogError("Transcription error: Code: {ErrorCode}, Message: {ErrorMessage}",
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
                        new[] { finalizedTextLength, finalizedTextLength + text.Length },
                        new[] { finalizedSpeechDuration, endTime })};

                updateExtractor.FinalizeWith(currentPart);
            }
            else {
                updateExtractor.EnqueueUpdate(text, endTime);
                break; // We process only the first one of non-final results
            }
        }
    }

    private async Task PushAudio(
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
