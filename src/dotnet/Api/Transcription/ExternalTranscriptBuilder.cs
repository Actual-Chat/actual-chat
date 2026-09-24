using System.Numerics;
using System.Runtime.InteropServices;

namespace ActualChat.Transcription;

/// <summary>
/// Turns an external producer's text deltas into <see cref="TranscriptDiff"/>s and a time map.
/// Offsets a producer omits are derived from the audio ingested so far.
/// </summary>
public sealed class ExternalTranscriptBuilder
{
    // Producers round their own offsets; a fraction of a second past the audio is not a lie
    private const float MaxOffsetOvershoot = 0.5f;

    private readonly List<Vector2> _points = [new(0, 0)];

    public Transcript Transcript { get; private set; } = Transcript.Empty;

    public TranscriptDiff? Append(ExternalTranscriptChunk chunk, TimeSpan ingestedAudioDuration)
    {
        var text = chunk.IsAppend ? Transcript.Text + chunk.Text : chunk.Text;
        var offset = (float)(chunk.AudioOffset ?? ingestedAudioDuration.TotalSeconds);
        if (!float.IsFinite(offset) || offset < 0)
            throw StandardError.Constraint("Audio offset must be finite and non-negative.");
        if (text.Length > Constants.Chat.MaxEntryTextLength)
            throw StandardError.Constraint(
                $"A message can hold up to {Constants.Chat.MaxEntryTextLength} characters.");

        var baseTranscript = Transcript;
        if (!chunk.IsAppend)
            TruncatePointsTo(text.Length);


        AddPoint(text.Length, offset);
        Transcript = new Transcript(text, new LinearMap(CollectionsMarshal.AsSpan(_points)), [])
            { IsStable = chunk.IsStable };
        var diff = TranscriptDiff.New(Transcript, baseTranscript);
        return diff.IsNone ? null : diff;
    }

    public Transcript Finalize(TimeSpan finalAudioDuration)
    {
        var duration = (float)finalAudioDuration.TotalSeconds;
        if (Transcript.Text.Length == 0)
            return Transcript;
        if (duration > 0 && _points[^1].Y > duration + MaxOffsetOvershoot)
            throw StandardError.Constraint(
                $"A transcript offset ({_points[^1].Y:F2}s) lies past the audio ({duration:F2}s).");

        // Every offset derived to the same value - the producer sent its text and its audio in
        // separate bursts. Spreading the text across the audio is approximate; refusing is worse.
        if (duration > 0 && _points[^1].Y <= float.Epsilon) {
            _points.Clear();
            _points.Add(new Vector2(0, 0));
            _points.Add(new Vector2(Transcript.Text.Length, duration));
        }
        else if (duration > _points[^1].Y)
            AddPoint(Transcript.Text.Length, duration);

        Transcript = Transcript with {
            TimeMap = new LinearMap(CollectionsMarshal.AsSpan(_points)),
            IsStable = true,
        };
        return Transcript;
    }

    // Private methods

    private void AddPoint(int textLength, float offset)
    {
        // The map must stay non-decreasing on both axes, or seeking inverts
        var last = _points[^1];
        var x = Math.Max(last.X, textLength);
        var y = Math.Max(last.Y, offset);
        if (x <= last.X && y <= last.Y)
            return;

        if (_points.Count > 1 && Math.Abs(last.X - x) < float.Epsilon)
            _points[^1] = new Vector2(x, y);
        else
            _points.Add(new Vector2(x, y));
    }

    private void TruncatePointsTo(int textLength)
    {
        // A replacement can shorten the text; points past its end would map characters that
        // no longer exist, which inverts seeking rather than merely being imprecise
        for (var i = _points.Count - 1; i > 0; i--) {
            if (_points[i].X <= textLength)
                break;

            _points.RemoveAt(i);
        }
    }
}
