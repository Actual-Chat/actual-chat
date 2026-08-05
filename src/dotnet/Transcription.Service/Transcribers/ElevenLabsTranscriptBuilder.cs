using System.Numerics;
using System.Text;

namespace ActualChat.Transcription;

// Scribe realtime sends whole transcripts rather than deltas: partial_transcript carries the
// current guess for the open segment, and a committed one replaces it for good. So committed
// segments accumulate here and the partial is appended on top of them.

public sealed class ElevenLabsTranscriptBuilder
{
    private readonly StringBuilder _committedText = new();
    private readonly List<Language> _languages = [];
    private LinearMap _committedMap = LinearMap.Zero;
    private float _committedEndTime;
    private string _partialText = "";

    public Transcript Update(string text)
    {
        _partialText = Separate(text);
        return NewTranscript(_committedText + _partialText, _committedMap, _committedEndTime, false);
    }

    public Transcript Commit(ElevenLabsMessage message)
    {
        var startOffset = _committedText.Length;
        var text = Separate(message.Text ?? "");
        _committedText.Append(text);
        _partialText = "";
        AddLanguage(message.LanguageCode);
        var offset = 0;
        foreach (var word in message.Words ?? []) {
            if (word.Type != "word" || word.Text.IsNullOrEmpty())
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

    public Transcript Complete()
    {
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
}
