using System.Numerics;
using Cysharp.Text;
using Google.Cloud.Speech.V2;
using Google.Protobuf;
using Exception = System.Exception;

namespace ActualChat.Transcription.Google;

public class GoogleTranscriberProcess : WorkerBase
{
    private static TimeSpan TranscriptCompletionDelay { get; } = TimeSpan.FromSeconds(2);

    private readonly TranscriberState _state;
    private readonly Channel<Transcript> _transcripts;

    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.TranscriberGoogle || Constants.DebugMode.TranscriberAny;

    private IAsyncEnumerable<byte[]> AudioByteStream { get; }
    private SpeechClient.StreamingRecognizeStream RecognizeStream { get; }
    private StreamingRecognitionConfig RecognitionConfig { get; }
    private MomentClockSet Clocks { get; }

    public GoogleTranscriberProcess(
        IAsyncEnumerable<byte[]> audioByteStream,
        SpeechClient.StreamingRecognizeStream recognizeStream,
        StreamingRecognitionConfig recognitionConfig,
        MomentClockSet clocks,
        ILogger? log = null)
    {
        Log = log ?? NullLogger.Instance;
        AudioByteStream = audioByteStream;
        RecognizeStream = recognizeStream;
        RecognitionConfig = recognitionConfig;
        Clocks = clocks;
        _state = new();
        _transcripts = Channel.CreateUnbounded<Transcript>(new UnboundedChannelOptions {
            SingleWriter = true,
            SingleReader = true,
        });
    }

    public IAsyncEnumerable<Transcript> GetTranscripts(
        CancellationToken cancellationToken = default)
        => _transcripts.Reader.ReadAllAsync(cancellationToken);

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var recognizeResponses = (IAsyncEnumerable<StreamingRecognizeResponse>)RecognizeStream.GetResponseStream();
        _ = BackgroundTask.Run(() => PushAudio(AudioByteStream),
            Log,
            $"{nameof(GoogleTranscriberProcess)}.{nameof(RunInternal)} failed",
            cancellationToken);

        await ProcessResponses(recognizeResponses, cancellationToken).ConfigureAwait(false);
    }

    internal async Task ProcessResponses(IAsyncEnumerable<StreamingRecognizeResponse> recognizeResponses, CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            await foreach (var response in recognizeResponses.WithCancellation(cancellationToken).ConfigureAwait(false))
                ProcessResponse(response);
        }
        catch (Exception e) {
            error = e;
            throw;
        }
        finally {
            if (error == null) {
                var finalTranscript = _state.Complete();
                _transcripts.Writer.TryWrite(finalTranscript);
            }
            _transcripts.Writer.TryComplete(error);
        }
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
                    _state.LastStable, response);
                return;
            }
            transcript = _state.AppendStable(text, textToTimeMap);
        }
        else {
            var text = results
                .Select(r => r.Alternatives.First().Transcript)
                .ToDelimitedString("");
            if (_state.LastStable.Text.Length != 0 && !text.OrdinalStartsWith(" ")) {
                // Google Transcribe issue: sometimes it returns alternatives w/o " " prefix,
                // i.e. they go concatenated with the stable (final) part.
                text = ZString.Concat(" ", text);
            }
            var resultEndOffset = results.First().ResultEndOffset;
            var endTime = resultEndOffset == null
                ? null
                : (float?) resultEndOffset.ToTimeSpan().TotalSeconds;

            transcript = _state.AppendAlternative(text, endTime);
        }
        DebugLog?.LogDebug("Transcript={Transcript}", transcript);
        _transcripts.Writer.TryWrite(transcript);
    }

    private bool TryParseFinal(StreamingRecognitionResult result,
        out string text, out LinearMap textToTimeMap)
    {
        var lastStable = _state.LastStable;
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

    private async Task PushAudio(IAsyncEnumerable<byte[]> webMByteStream)
    {
        try {
            await foreach (var chunk in webMByteStream.ConfigureAwait(false)) {
                var request = new StreamingRecognizeRequest {
                    StreamingConfig = RecognitionConfig,
                    Audio = ByteString.CopyFrom(chunk),
                };
                await RecognizeStream.WriteAsync(request).ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            _transcripts.Writer.TryComplete(e);
        }
        finally {
            await Clocks.CpuClock.Delay(TranscriptCompletionDelay, StopToken).ConfigureAwait(false);
            await RecognizeStream.WriteCompleteAsync().ConfigureAwait(false);
        }
    }
}
