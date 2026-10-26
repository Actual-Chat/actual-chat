using System.Numerics;
using System.Text;

namespace ActualChat.Transcription;

// For providers that send whole segments rather than deltas (ElevenLabs Scribe realtime, xAI):
// a partial carries the current guess for the open segment, and a committed one replaces it for
// good. So committed segments accumulate here and the partial is appended on top of them.
// Recommit replaces every segment committed with a start at or after its own: xAI finalizes an
// utterance chunk by chunk, then re-sends the whole of it once the speaker stops.

public sealed class SegmentTranscriptBuilder
{
    private readonly StringBuilder _committedText = new();
    private readonly List<Language> _languages = [];
    private readonly List<Segment> _segments = [];
    private LinearMap _committedMap = LinearMap.Zero;
    private float _committedEndTime;
    private string _partialText = "";

    public Transcript Update(string text)
    {
        _partialText = Separate(text);
        return NewTranscript(_committedText + _partialText, _committedMap, _committedEndTime, false);
    }

    public Transcript Commit(
        string text,
        IEnumerable<(string? Text, double Start, double End)> words,
        string? languageCode = null,
        double? start = null)
    {
        if (start is { } segmentStart)
            _segments.Add(new Segment(segmentStart, _committedText.Length, _committedMap, _committedEndTime));

        var startOffset = _committedText.Length;
        text = Separate(text);
        _committedText.Append(text);
        _partialText = "";
        AddLanguage(languageCode);
        var offset = 0;
        foreach (var word in words) {
            if (word.Text.IsNullOrEmpty())
                continue;

            var wordStart = text.IndexOf(word.Text, offset);
            if (wordStart < 0)
                continue;

            offset = wordStart + word.Text.Length;
            _committedMap = TryAppend(_committedMap, startOffset + wordStart, (float)word.Start);
            _committedMap = TryAppend(_committedMap, startOffset + offset, (float)word.End);
            _committedEndTime = (float)word.End;
        }

        return NewTranscript(_committedText.ToString(), _committedMap, _committedEndTime, false);
    }

    public Transcript Recommit(
        double start,
        string text,
        IEnumerable<(string? Text, double Start, double End)> words,
        string? languageCode = null)
    {
        var index = _segments.FindIndex(x => x.Start >= start);
        if (index >= 0) {
            var first = _segments[index];
            _segments.RemoveRange(index, _segments.Count - index);
            _committedText.Length = first.TextLength;
            _committedMap = first.Map;
            _committedEndTime = first.EndTime;
        }

        return Commit(text, words, languageCode, start);
    }

    public Transcript Complete(string? languageCode = null)
    {
        AddLanguage(languageCode);
        if (_partialText.Length != 0) {
            _committedText.Append(_partialText);
            _partialText = "";
        }

        return NewTranscript(_committedText.ToString(), _committedMap, _committedEndTime, true);
    }

    // Private methods

    private string Separate(string text)
    {
        if (text.IsNullOrEmpty() || _committedText.Length == 0)
            return text;

        return char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(_committedText[^1])
            ? text
            : " " + text;
    }

    private Transcript NewTranscript(string text, LinearMap map, float endTime, bool isStable)
    {
        if (map.IsDegenerate && !text.IsNullOrEmpty())
            map = new LinearMap(new Vector2(0, 0), new Vector2(text.Length, endTime));

        return new Transcript(text, map, _languages.ToArray()) { IsStable = isStable };
    }

    private void AddLanguage(string? code)
    {
        var language = Language.TryParse(code, true);
        if (language != null && !_languages.Contains(language))
            _languages.Add(language);
    }

    private static LinearMap TryAppend(LinearMap map, float x, float y)
    {
        // LinearMap requires strictly increasing points; words can repeat a boundary or timestamp.
        var points = map.Length;
        if (points > 0) {
            var last = map[points - 1];
            if (x <= last.X || y < last.Y)
                return map;
        }

        return map.Append(new Vector2(x, y));
    }

    // Nested types

    // The committed state as it was right before the segment was appended.
    private readonly record struct Segment(double Start, int TextLength, LinearMap Map, float EndTime);
}
