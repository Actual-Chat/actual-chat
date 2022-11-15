using System.Numerics;
using ActualChat.Audio;
using Cysharp.Text;
using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V1P1Beta1;
using Google.Protobuf;

namespace ActualChat.Transcription.Google;

public class GoogleTranscriberProcess : WorkerBase
{
    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.TranscriberGoogle || Constants.DebugMode.TranscriberAny;

    private TranscriptionOptions Options { get; }
    private AudioSource AudioSource { get; }
    private TranscriberState State { get; }
    private Channel<Transcript> Transcripts { get; }

    public GoogleTranscriberProcess(
        TranscriptionOptions options,
        AudioSource audioSource,
        ILogger? log = null)
    {
        Log = log ?? NullLogger.Instance;
        Options = options;
        AudioSource = audioSource;
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
            var webMStreamAdapter = new WebMStreamAdapter(Log);
            await AudioSource.WhenFormatAvailable.ConfigureAwait(false);
            var format = AudioSource.Format;
            var byteStream = webMStreamAdapter.Write(AudioSource, cancellationToken);
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
            };

            var recognizeRequests = speechClient.StreamingRecognize(CallSettings.FromCancellationToken(cancellationToken));
            await recognizeRequests.WriteAsync(new () {
                    StreamingConfig = new () {
                        Config = config,
                        InterimResults = true,
                        SingleUtterance = false,
                    },
                }).ConfigureAwait(false);
            var recognizeResponses = (IAsyncEnumerable<StreamingRecognizeResponse>)recognizeRequests.GetResponseStream();

            _ = BackgroundTask.Run(() => PushAudio(byteStream, recognizeRequests),
                Log,
                $"{nameof(GoogleTranscriberProcess)}.{nameof(RunInternal)} failed",
                cancellationToken);

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
        DebugLog?.LogDebug("Response={Response}", response);
        var error = response.Error;
        if (error != null)
            throw new TranscriptionException($"G{error.Code:D}", error.Message);

        Transcript transcript;
        var results = response.Results;
        var hasFinal = results.Any(r => r.IsFinal);
        if (hasFinal) {
            var result = results.Single(r => r.IsFinal);
            if (!TryParseFinal(result, out var text, out var textToTimeMap)) {
                Log.LogWarning("Final transcript discarded. State.LastStable={LastStable}, Response={Response}",
                    State.LastStable, response);
                return;
            }
            transcript = State.AppendStable(text, textToTimeMap);
        }
        else {
            var text = results
                .Select(r => r.Alternatives.First().Transcript)
                .ToDelimitedString("");
            if (State.LastStable.Text.Length != 0 && !text.OrdinalStartsWith(" ")) {
                // Google Transcribe issue: sometimes it returns alternatives w/o " " prefix,
                // i.e. they go concatenated with the stable (final) part.
                text = ZString.Concat(" ", text);
            }
            var endTime = (float)results.First().ResultEndTime.ToTimeSpan().TotalSeconds;
            transcript = State.AppendAlternative(text, endTime);
        }
        DebugLog?.LogDebug("Transcript={Transcript}", transcript);
        Transcripts.Writer.TryWrite(transcript);
    }

    private bool TryParseFinal(StreamingRecognitionResult result,
        out string text, out LinearMap textToTimeMap)
    {
        var lastStable = State.LastStable;
        var lastStableTextLength = lastStable.Text.Length;
        var lastStableDuration = lastStable.TextToTimeMap.YRange.End;

        var alternative = result.Alternatives.Single();
        var endTime = (float)result.ResultEndTime.ToTimeSpan().TotalSeconds;
        text = alternative.Transcript;
        if (lastStableTextLength > 0 && text.Length > 0 && !char.IsWhiteSpace(text[0]))
            text = " " + text;

        var mapPoints = new List<Vector2>();
        var parsedOffset = 0;
        var parsedDuration = lastStableDuration;
        foreach (var word in alternative.Words) {
            var wordStartTime = (float)word.StartTime.ToTimeSpan().TotalSeconds;
            if (wordStartTime < parsedDuration)
                continue;
            var wordStart = text.OrdinalIgnoreCaseIndexOf(word.Word, parsedOffset);
            if (wordStart < 0)
                continue;

            var wordEndTime = (float)word.EndTime.ToTimeSpan().TotalSeconds;
            var wordEnd = wordStart + word.Word.Length;

            mapPoints.Add(new Vector2(lastStableTextLength + wordStart, wordStartTime));
            mapPoints.Add(new Vector2(lastStableTextLength + wordEnd, wordEndTime));

            parsedDuration = wordStartTime;
            parsedOffset = wordStart + word.Word.Length;
        }

        if (mapPoints.Count == 0) {
            textToTimeMap = default;
            return false;
        }

        var lastPoint = mapPoints[^1];
        var veryLastPoint = new Vector2(lastStableTextLength + text.Length, endTime);
        if (Math.Abs(lastPoint.X - veryLastPoint.X) < 0.1)
            mapPoints[^1] = veryLastPoint;
        else
            mapPoints.Add(veryLastPoint);
        textToTimeMap = new LinearMap(mapPoints.ToArray()).Simplify(Transcript.TextToTimeMapTimePrecision);
        return true;
    }

    private async Task PushAudio(
        IAsyncEnumerable<byte[]> webMByteStream,
        SpeechClient.StreamingRecognizeStream recognizeRequests)
    {
        try {
            await foreach (var chunk in webMByteStream.ConfigureAwait(false)) {
                var request = new StreamingRecognizeRequest {
                    AudioContent = ByteString.CopyFrom(chunk),
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
