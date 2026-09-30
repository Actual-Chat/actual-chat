namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// Cuts one segment of a live transcript into runs, each either plain or under a coach mark; a
/// segment is a slice of the full text, so the spans (offsets in the full text) are shifted by its offset.
/// </summary>
public static class CoachStreamingMarks
{
    public readonly record struct Piece(string Text, string Class);

    public static IReadOnlyList<Piece> Split(string segment, int offset, ApiArray<SpeechSpan> spans)
    {
        if (segment.IsNullOrEmpty())
            return [];

        var pieces = new List<Piece>();
        var position = 0;
        foreach (var span in spans.OrderBy(s => s.Start)) {
            var start = Math.Max(position, span.Start - offset);
            var end = Math.Min(segment.Length, span.Start + span.Length - offset);
            if (end <= start)
                continue;

            if (start > position)
                pieces.Add(new Piece(segment[position..start], ""));
            pieces.Add(new Piece(segment[start..end], ClassOf(span.Kind)));
            position = end;
        }
        if (position < segment.Length)
            pieces.Add(new Piece(segment[position..], ""));
        return pieces;
    }

    public static string ClassOf(SpeechSpanKind kind)
        => kind switch {
            SpeechSpanKind.FilledPause or SpeechSpanKind.Filler => "coach-filler",
            SpeechSpanKind.Weak or SpeechSpanKind.Repetition => "coach-weak",
            SpeechSpanKind.Profanity => "coach-profane",
            _ => "",
        };
}
