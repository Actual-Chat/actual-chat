namespace ActualChat.Chat;

// A word the tagger marked in a transcript that is still being spoken: found again by word and occurrence,
// because the text that settles can differ a little from the text that was tagged
[DataContract, MessagePackObject]
public sealed partial record CoachLiveMark(
    [property: DataMember, Key(0)] SpeechSpanKind Kind,
    [property: DataMember, Key(1)] string Word,
    [property: DataMember, Key(2)] int Occurrence,
    [property: DataMember, Key(3)] ApiArray<string> Synonyms
);

public static class CoachLiveMarks
{
    public static ApiArray<CoachLiveMark> FromSpans(string text, IEnumerable<SpeechSpan> spans, bool isWholeWord)
    {
        // The occurrences of each distinct word are found once, however many spans repeat it
        var startsByWord = new Dictionary<string, List<int>>();
        var marks = new List<CoachLiveMark>();
        foreach (var span in spans) {
            if (!startsByWord.TryGetValue(span.Word, out var starts)) {
                starts = SpanLocator.LocateAll(text, span.Word, isWholeWord).Select(r => r.Start).ToList();
                startsByWord[span.Word] = starts;
            }
            var index = starts.BinarySearch(span.Start);
            var occurrence = (index >= 0 ? index : ~index) + 1;
            marks.Add(new CoachLiveMark(span.Kind, span.Word, occurrence, span.Synonyms));
        }
        return marks.ToApiArray();
    }

    public static ApiArray<SpeechSpan> Locate(string text, IEnumerable<CoachLiveMark> marks, bool isWholeWord)
    {
        var spans = new List<SpeechSpan>();
        foreach (var mark in marks) {
            if (SpanLocator.Locate(text, mark.Word, mark.Occurrence, isWholeWord) is not { } range)
                continue;

            spans.Add(new SpeechSpan(mark.Kind, mark.Word, range.Start, range.End - range.Start, mark.Synonyms));
        }
        var ordered = spans.OrderBy(s => s.Start).ToList();
        var result = new List<SpeechSpan>();
        foreach (var span in ordered)
            if (result.Count == 0 || span.Start >= result[^1].Start + result[^1].Length)
                result.Add(span);
        return result.ToApiArray();
    }
}
