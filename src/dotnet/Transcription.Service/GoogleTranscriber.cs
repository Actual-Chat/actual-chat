using System.Globalization;
using ActualChat.Audio;
using ActualChat.Blobs;
using Google.Cloud.Speech.V1P1Beta1;
using Google.Protobuf;

namespace ActualChat.Transcription;

public class GoogleTranscriber : ITranscriber
{
    private readonly ILogger<GoogleTranscriber> _log;

    public GoogleTranscriber(ILogger<GoogleTranscriber> log)
    {
        _log = log;
    }

    public async Task<ChannelReader<TranscriptUpdate>> Transcribe(
        TranscriptionRequest request,
        ChannelReader<BlobPart> audioData,
        CancellationToken cancellationToken)
    {
        var (streamId, format, options) = request;
        _log.LogInformation("Start transcription of StreamId = {StreamId}", (string) streamId);

        var builder = new SpeechClientBuilder();
        var speechClient = await builder.BuildAsync(cancellationToken);
        var config = new RecognitionConfig {
            Encoding = MapEncoding(format.CodecKind),
            AudioChannelCount = format.ChannelCount,
            SampleRateHertz = format.SampleRate,
            LanguageCode = options.Language,
            EnableAutomaticPunctuation = options.IsPunctuationEnabled,
            DiarizationConfig = new SpeakerDiarizationConfig {
                EnableSpeakerDiarization = true,
                MaxSpeakerCount = options.MaxSpeakerCount ?? 5
            }
        };

        var streamingRecognizeStream = speechClient.StreamingRecognize();
        await streamingRecognizeStream.WriteAsync(new StreamingRecognizeRequest {
            StreamingConfig = new StreamingRecognitionConfig {
                Config = config,
                InterimResults = true,
                SingleUtterance = false
            },
        }).ConfigureAwait(false);
        var responseStream =  (IAsyncEnumerable<StreamingRecognizeResponse>)streamingRecognizeStream.GetResponseStream();
        var transcriptChannel = Channel.CreateUnbounded<TranscriptUpdate>(
            new UnboundedChannelOptions{ SingleWriter = true });

        _ = Task.Run(()
                => PushAudioForTranscription(streamingRecognizeStream,
                    audioData,
                    transcriptChannel.Writer,
                    cancellationToken),
            cancellationToken);
        _ = Task.Run(()
                => ReadTranscript(responseStream, transcriptChannel.Writer, cancellationToken),
            cancellationToken);

        return transcriptChannel.Reader;
    }

    private async Task PushAudioForTranscription(
        SpeechClient.StreamingRecognizeStream recognizeStream,
        ChannelReader<BlobPart> audioData,
        ChannelWriter<TranscriptUpdate> writer,
        CancellationToken cancellationToken)
    {
        try {
            while (await audioData.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (audioData.TryRead(out var blobPart))
                await recognizeStream.WriteAsync(new StreamingRecognizeRequest {
                    AudioContent = ByteString.CopyFrom(blobPart.Data)
                }).ConfigureAwait(false);
        }
        catch (ChannelClosedException) { }
        catch (Exception e) {
            writer.TryComplete(e);
        }
        finally {
            await recognizeStream.WriteCompleteAsync().ConfigureAwait(false);
        }
    }

    private async Task ReadTranscript(
        IAsyncEnumerable<StreamingRecognizeResponse> transcriptResponseStream,
        ChannelWriter<TranscriptUpdate> writer,
        CancellationToken cancellationToken)
    {
        var cutter = new StablePrefixCutter();
        Exception? error = null;
        try {
            await foreach (var response in transcriptResponseStream.WithCancellation(cancellationToken)) {
                var speechFragment = MapResponse(response);
                if (speechFragment.StartOffset != 0)
                    await writer.WriteAsync(speechFragment, cancellationToken).ConfigureAwait(false);
                else {
                    var processedFragment = cutter.CutMemoized(speechFragment);
                    await writer.WriteAsync(processedFragment, cancellationToken).ConfigureAwait(false);
                }
            }

        }
        catch (Exception e) {
            error = e;
        }
        finally {
            writer.Complete(error);
        }

        TranscriptUpdate MapResponse(StreamingRecognizeResponse response)
        {
            if (response.Error != null) {
                _log.LogError("Transcription error: Code {ErrorCode}; Message: {ErrorMessage}", response.Error.Code, response.Error.Message);
                throw new TranscriptionException(
                    response.Error.Code.ToString(CultureInfo.InvariantCulture),
                    response.Error.Message);
            }

            var result = response.Results.First();
            var alternative = result.Alternatives.First();
            var endTime = result.ResultEndTime;
            var endOffset = endTime.ToTimeSpan().TotalSeconds;
            var fragment = new TranscriptUpdate {
                Confidence = alternative.Confidence,
                Text = alternative.Transcript,
                StartOffset = 0, // TODO(AK) : suspicious 0 offset
                Duration = endOffset,
                IsFinal = result.IsFinal
            };
            return fragment;
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
