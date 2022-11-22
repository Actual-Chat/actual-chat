using System.Numerics;
using ActualChat.Audio;
using Cysharp.Text;
using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V2;
using Google.Protobuf;

namespace ActualChat.Transcription.Google;

public class GoogleTranscriberProcess : WorkerBase
{
    private readonly Task<Recognizer> _recognizerTask;

    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.TranscriberGoogle || Constants.DebugMode.TranscriberAny;

    private TranscriptionOptions Options { get; }
    private AudioSource AudioSource { get; }
    private TranscriberState State { get; }
    private Channel<Transcript> Transcripts { get; }

    public GoogleTranscriberProcess(
        Task<Recognizer> recognizerTask,
        TranscriptionOptions options,
        AudioSource audioSource,
        ILogger? log = null)
    {
        _recognizerTask = recognizerTask;
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
            var recognizer = await _recognizerTask.ConfigureAwait(false);
            var webMStreamAdapter = new WebMStreamAdapter(Log);
            await AudioSource.WhenFormatAvailable.ConfigureAwait(false);
            var byteStream = webMStreamAdapter.Write(AudioSource, cancellationToken);
            var builder = new SpeechClientBuilder();
            var speechClient = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
            var recognizeRequests = speechClient
                .StreamingRecognize(CallSettings.FromCancellationToken(cancellationToken));
            var streamingRecognitionConfig = new StreamingRecognitionConfig {
                Config = new RecognitionConfig {
                    AutoDecodingConfig = new AutoDetectDecodingConfig()
                }, // Use recognizer' settings
                StreamingFeatures = new StreamingRecognitionFeatures {
                    InterimResults = true,
                    // TODO(AK): test google VAD events - probably it might be useful
                    // VoiceActivityTimeout =
                    // EnableVoiceActivityEvents =
                },
            };
            await recognizeRequests.WriteAsync(new StreamingRecognizeRequest {
                    StreamingConfig = streamingRecognitionConfig,
                    Recognizer = recognizer.Name,
                }).ConfigureAwait(false);
            var recognizeResponses = (IAsyncEnumerable<StreamingRecognizeResponse>)recognizeRequests.GetResponseStream();

            _ = BackgroundTask.Run(() => PushAudio(byteStream, recognizeRequests, streamingRecognitionConfig),
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
            var resultEndOffset = results.First().ResultEndOffset;
            var endTime = resultEndOffset == null
                ? null
                : (float?) resultEndOffset.ToTimeSpan().TotalSeconds;

            transcript = State.AppendAlternative(text, endTime);
            DebugLog?.LogDebug("Transcript={Transcript}", transcript);
            Transcripts.Writer.TryWrite(transcript);
        }
    }

    private bool TryParseFinal(StreamingRecognitionResult result,
        out string text, out LinearMap textToTimeMap)
    {
        var lastStable = State.LastStable;
        var lastStableTextLength = lastStable.Text.Length;
        var lastStableDuration = lastStable.TextToTimeMap.YRange.End;

        var alternative = result.Alternatives.Single();
        var resultEndOffset = result.ResultEndOffset;
        var endTime = resultEndOffset == null
            ? null
            : (float?) resultEndOffset.ToTimeSpan().TotalSeconds;
        text = alternative.Transcript;
        if (lastStableTextLength > 0 && text.Length > 0 && !char.IsWhiteSpace(text[0]))
            text = " " + text;

        var mapPoints = new List<Vector2>();
        var parsedOffset = 0;
        var parsedDuration = lastStableDuration;
        foreach (var word in alternative.Words) {
            var wordStartTime = word.StartOffset == null
                ? 0
                : (float)word.StartOffset.ToTimeSpan().TotalSeconds;
            if (wordStartTime < parsedDuration)
                continue;
            var wordStart = text.OrdinalIgnoreCaseIndexOf(word.Word, parsedOffset);
            if (wordStart < 0)
                continue;

            var wordEndTime = (float)word.EndOffset.ToTimeSpan().TotalSeconds;
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
        var veryLastPoint = new Vector2(lastStableTextLength + text.Length, endTime ?? mapPoints.Max(v => v.Y));
        if (Math.Abs(lastPoint.X - veryLastPoint.X) < 0.1)
            mapPoints[^1] = veryLastPoint;
        else
            mapPoints.Add(veryLastPoint);
        textToTimeMap = new LinearMap(mapPoints.ToArray()).Simplify(Transcript.TextToTimeMapTimePrecision);
        return true;
    }

    private async Task PushAudio(
        IAsyncEnumerable<byte[]> webMByteStream,
        SpeechClient.StreamingRecognizeStream recognizeRequests,
        StreamingRecognitionConfig streamingRecognitionConfig)
    {
        try {
            await foreach (var chunk in webMByteStream.ConfigureAwait(false)) {
                var request = new StreamingRecognizeRequest {
                    StreamingConfig = streamingRecognitionConfig,
                    Audio = ByteString.CopyFrom(chunk),
                };
                await recognizeRequests.WriteAsync(request).ConfigureAwait(false);
            }
        }
        finally {
            await recognizeRequests.WriteCompleteAsync().ConfigureAwait(false);
        }
    }
}
