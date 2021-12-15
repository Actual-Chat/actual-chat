using System.Numerics;
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

    private TranscriptionOptions Options { get; }
    private IAsyncEnumerable<AudioStreamPart> AudioStream { get; }
    private GoogleTranscriberState State { get; }
    private Channel<Transcript> Transcripts { get; }

    public GoogleTranscriberProcess(
        TranscriptionOptions options,
        IAsyncEnumerable<AudioStreamPart> audioStream,
        ILogger? log = null)
    {
        Log = log ?? NullLogger.Instance;
        Options = options;
        AudioStream = audioStream;
        State = new();
        Transcripts = Channel.CreateUnbounded<Transcript>(new UnboundedChannelOptions() {
            SingleWriter = true,
            SingleReader = true,
        });
    }

    public IAsyncEnumerable<Transcript> GetTranscripts(
        CancellationToken cancellationToken = default)
        => Transcripts.Reader.ReadAllAsync(cancellationToken);

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        try {
            var (format, frames) = await AudioStream.ToFormatAndFrames(cancellationToken).ConfigureAwait(false);
            await using var __ = frames.ConfigureAwait(false);

            var builder = new SpeechClientBuilder();
            var speechClient = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
            var config = new RecognitionConfig {
                Encoding = MapEncoding(format.CodecKind),
                AudioChannelCount = format.ChannelCount,
                SampleRateHertz = format.SampleRate,
                LanguageCode = Options.Language,
                UseEnhanced = true,
                MaxAlternatives = 1,
                EnableAutomaticPunctuation = Options.IsPunctuationEnabled,
                EnableSpokenPunctuation = false,
                EnableSpokenEmojis = false,
                EnableWordTimeOffsets = true,
                DiarizationConfig = new() {
                    EnableSpeakerDiarization = true,
                    MaxSpeakerCount = Options.MaxSpeakerCount ?? 5,
                },
                Metadata = new() {
                    InteractionType = RecognitionMetadata.Types.InteractionType.Discussion,
                    MicrophoneDistance = RecognitionMetadata.Types.MicrophoneDistance.Nearfield,
                    RecordingDeviceType = RecognitionMetadata.Types.RecordingDeviceType.Smartphone,
                },
            };
            foreach (var altLanguage in Options.AltLanguages)
                config.AlternativeLanguageCodes.Add(altLanguage);

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
        catch (Exception e) {
            Transcripts.Writer.TryComplete(e);
            if (e is not OperationCanceledException)
                Log.LogError(e, "Transcription failed");
            throw;
        }
    }

    internal async Task ProcessResponses(IAsyncEnumerable<StreamingRecognizeResponse> recognizeResponses, CancellationToken cancellationToken)
    {
        await foreach (var response in recognizeResponses.WithCancellation(cancellationToken).ConfigureAwait(false))
            ProcessResponse(response);

        var finalTranscript = State.Complete();
        Transcripts.Writer.TryWrite(finalTranscript);
        Transcripts.Writer.Complete();
    }

    private void ProcessResponse(StreamingRecognizeResponse response)
    {
        DebugLog?.LogDebug("Response: {Response}", response);
        var error = response.Error;
        if (error != null)
            throw new TranscriptionException($"G{error.Code:D}", error.Message);

        Transcript transcript;
        var results = response.Results;
        var isFinal = results.Any(r => r.IsFinal);
        if (isFinal) {
            var result = results.Single();
            if (!TryParseFinal(result, out var text, out var textToTimeMap))
                return;
            transcript = State.AppendFinal(text, textToTimeMap);
        }
        else {
            var text = results
                .Select(r => r.Alternatives.First().Transcript)
                .ToDelimitedString("");
            var endTime = (float) results.First().ResultEndTime.ToTimeSpan().TotalSeconds;
            transcript = State.AppendAlternative(text, endTime);
        }
        DebugLog?.LogDebug("Transcript: {Transcript}", transcript);
        Transcripts.Writer.TryWrite(transcript);
    }

    private bool TryParseFinal(StreamingRecognitionResult result,
        out string text, out LinearMap textToTimeMap)
    {
        var alternative = result.Alternatives.Single();
        var endTime = (float) result.ResultEndTime.ToTimeSpan().TotalSeconds;
        text = alternative.Transcript;

        var lastFinal = State.LastFinal;
        var lastFinalTextLength = lastFinal.Text.Length;
        var lastFinalDuration = lastFinal.TextToTimeMap.YRange.End;
        var mapPoints = new List<Vector2> { new(lastFinalTextLength, lastFinalDuration) };
        var lastWordOffset = 0;
        var lastWordStartTime = -1d;
        foreach (var word in alternative.Words) {
            var wordStartTime = (float) word.StartTime.ToTimeSpan().TotalSeconds;
            if (lastWordStartTime - 0.1 > wordStartTime) {
                DebugLog?.LogDebug("Invalid response skipped, word: {Word}", word);
                textToTimeMap = default;
                return false;
            }

            lastWordStartTime = wordStartTime;
            if (wordStartTime < lastFinalDuration)
                continue;
            var wordOffset = text.IndexOf(word.Word, lastWordOffset, StringComparison.InvariantCultureIgnoreCase);
            if (wordOffset < 0)
                continue;
            lastWordOffset = wordOffset + word.Word.Length;
            var finalTextWordOffset = lastFinalTextLength + wordOffset;
            if (mapPoints[^1].X >= finalTextWordOffset)
                continue;

            mapPoints.Add(new Vector2(finalTextWordOffset, (float) word.StartTime.ToTimeSpan().TotalSeconds));
        }

        mapPoints.Add(new Vector2(lastFinalTextLength + text.Length, endTime));
        textToTimeMap = new LinearMap(mapPoints.ToArray())
            .Simplify(Transcript.TextToTimeMapTimePrecision);
        return true;
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
