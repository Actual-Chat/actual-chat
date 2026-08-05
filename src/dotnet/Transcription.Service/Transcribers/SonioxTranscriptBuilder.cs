using System.Numerics;
using System.Text;

namespace ActualChat.Transcription;

// Soniox streams tokens rather than transcripts: is_final tokens arrive once and never change,
// while the non-final tail is replaced by every subsequent message. So finals accumulate here
// and the tail is rebuilt on each update.
//
// enable_endpoint_detection also emits an "<end>" token once per finalized segment. It's a
// structural marker rather than speech, so it's dropped before it can reach the transcript.

public sealed class SonioxTranscriptBuilder
{
    private const string EndpointToken = "<end>";
    private readonly StringBuilder _finalText = new();
    private readonly List<Language> _languages = [];
    private LinearMap _finalMap = LinearMap.Zero;
    private float _finalEndTime;
    private string _tailText = "";
    private LinearMap _tailMap = LinearMap.Zero;
    private float _tailEndTime;

    public Transcript Update(IReadOnlyList<SonioxToken> tokens)
    {
        var tail = new StringBuilder();
        var map = _finalMap;
        var tailStartOffset = _finalText.Length;
        var endTime = _finalEndTime;
        foreach (var token in tokens) {
            if (token.Text.IsNullOrEmpty() || token.Text == EndpointToken)
                continue;

            AddLanguage(token.Language);
            if (token.IsFinal) {
                // A final token can only arrive while the tail is still empty for this message:
                // Soniox emits finals before the non-final tail it supersedes.
                var startOffset = _finalText.Length;
                _finalText.Append(token.Text);
                _finalMap = AppendToken(_finalMap, startOffset, _finalText.Length, token);
                _finalEndTime = ToSeconds(token.EndMs);
                map = _finalMap;
                tailStartOffset = _finalText.Length;
                endTime = _finalEndTime;
                continue;
            }

            var tailOffset = tailStartOffset + tail.Length;
            tail.Append(token.Text);
            map = AppendToken(map, tailOffset, tailStartOffset + tail.Length, token);
            endTime = ToSeconds(token.EndMs);
        }

        _tailText = tail.ToString();
        _tailMap = map;
        _tailEndTime = endTime;
        var text = tail.Length == 0 ? _finalText.ToString() : _finalText + _tailText;
        return NewTranscript(text, map, endTime, false);
    }

    public Transcript Complete(bool hasFinished = true)
        // A stream that ended without a finished response gets no further chance to finalize,
        // so its tail is the best transcript available and dropping it would lose words.
        => hasFinished || _tailText.Length == 0
            ? NewTranscript(_finalText.ToString(), _finalMap, _finalEndTime, true)
            : NewTranscript(_finalText + _tailText, _tailMap, _tailEndTime, true);

    // Private methods

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

    private static LinearMap AppendToken(LinearMap map, int startOffset, int endOffset, SonioxToken token)
        => TryAppend(TryAppend(map, startOffset, ToSeconds(token.StartMs)), endOffset, ToSeconds(token.EndMs));

    private static LinearMap TryAppend(LinearMap map, float x, float y)
    {
        // LinearMap requires strictly increasing points; tokens can repeat a boundary offset or timestamp.
        var points = map.Length;
        if (points > 0) {
            var last = map[points - 1];
            if (x <= last.X || y < last.Y)
                return map;
        }

        return map.Append(new Vector2(x, y));
    }

    private static float ToSeconds(long ms)
        => (float)(ms / 1000.0);
}
