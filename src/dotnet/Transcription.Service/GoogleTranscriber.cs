using System.Globalization;
using ActualChat.Audio;
using ActualChat.Mathematics;
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
        AudioSource audioSource,
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
                    audioSource,
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
        AudioSource audioSource,
        ChannelWriter<TranscriptUpdate> writer,
        CancellationToken cancellationToken)
    {
        try {
            var header = Convert.FromBase64String(audioSource.Format.CodecSettings);
            await recognizeStream.WriteAsync(new StreamingRecognizeRequest {
                AudioContent = ByteString.CopyFrom(header),
            }).ConfigureAwait(false);

            await foreach (var audioFrame in audioSource.Frames.WithCancellation(cancellationToken))
                await recognizeStream.WriteAsync(new StreamingRecognizeRequest {
                    AudioContent = ByteString.CopyFrom(audioFrame.Data),
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
        var updateExtractor = new TranscriptUpdateExtractor();

        Exception? error = null;
        try {
            await foreach (var response in transcriptResponseStream.WithCancellation(cancellationToken)) {
                ProcessResponse(response);
                while (updateExtractor.Updates.TryDequeue(out var update))
                    await writer.WriteAsync(update, cancellationToken).ConfigureAwait(false);
            }

        }
        catch (Exception e) {
            error = e;
        }
        finally {
            updateExtractor.FinalizeCurrentPart();
            while (updateExtractor.Updates.TryDequeue(out var update))
                await writer.WriteAsync(update, cancellationToken).ConfigureAwait(false);
            writer.Complete(error);
        }

        void ProcessResponse(StreamingRecognizeResponse response)
        {
            if (response.Error != null) {
                _log.LogError("Transcription error: Code {ErrorCode}; Message: {ErrorMessage}", response.Error.Code, response.Error.Message);
                throw new TranscriptionException(
                    response.Error.Code.ToString(CultureInfo.InvariantCulture),
                    response.Error.Message);
            }

            foreach (var result in response.Results) {
                var alternative = result.Alternatives.First();
                var endTime = result.ResultEndTime;
                var text = alternative.Transcript;
                var finalizedPart = updateExtractor.FinalizedPart;
                var currentPart = new Transcript() {
                    Text = text,
                    TextToTimeMap =  new LinearMap(
                        new [] {(double) finalizedPart.Text.Length, finalizedPart.Text.Length + text.Length},
                        new [] {finalizedPart.Duration, endTime.ToTimeSpan().TotalSeconds})
                };
                updateExtractor.UpdateCurrentPart(currentPart);
                if (result.IsFinal)
                    updateExtractor.FinalizeCurrentPart();
                else
                    break; // We process only the first one of non-final results
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
