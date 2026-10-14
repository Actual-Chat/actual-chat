using System.Numerics;
using System.Text;

namespace ActualChat.Transcription;

// Soniox streams tokens rather than transcripts: is_final tokens arrive once and never change,
// while the non-final tail is replaced by every subsequent message. So finals accumulate here
// and the tail is rebuilt on each update.
//
// enable_endpoint_detection also emits an "<end>" token once per finalized segment. It's a
// structural marker rather than speech, so it never enters the text; the stable transcript of
// its message carries it as IsSegmentEnd instead - re-emitted if the message brought no new
// finals, so the signal isn't lost. Complete() is not flagged: the stream end is its own signal.
//
// Finals never change, so a message that brings new ones yields a stable finals-only transcript
// first; the tail, if any, follows in an unstable one. Stability is what the realtime translation
// and the dub build on, and Soniox hands it out phrase by phrase - not only at the end of the stream.
//
// Soniox finalizes 3-5s behind the speech, though, and practically never revises a tail token
// that's more than ~1s old - so the leading tail tokens older than the stable token age
// (`TranscriptionSettings.SonioxStableTokenAge`, relative to the message's processed-audio
// position) are promoted to finals as well. Once promoted, a span is settled: the tail re-sent
// by the next messages and the eventual finals for it are ignored, and a late revision of a
// promoted word is lost - offline re-transcription fixes the stored text.

public sealed class SonioxTranscriptBuilder(TimeSpan stableTokenAge)
{
    private const string EndpointToken = "<end>";
    private readonly long _stableTokenAgeMs = (long)stableTokenAge.TotalMilliseconds;
    private readonly StringBuilder _finalText = new();
    private readonly List<Language> _languages = [];
    private readonly List<SonioxToken> _finalTimingTokens = [];
    private List<SonioxToken> _tailTimingTokens = [];
    private LinearMap _finalMap;
    private float _finalEndTime;
    private long _promotedEndMs;
    private string _tailText = "";
    private LinearMap _tailMap = LinearMap.Zero;
    private float _tailEndTime;

    public static LinearMap CreateDetailedTimeMap(IReadOnlyList<SonioxToken> tokens)
    {
        var map = default(LinearMap);
        var offset = 0;
        foreach (var token in tokens) {
            if (token.Text == EndpointToken || token.Text.IsNullOrEmpty())
                continue;

            if (token.StartMs < 0 || token.EndMs < token.StartMs)
                throw new ArgumentOutOfRangeException(nameof(tokens));

            var text = token.Text.AsSpan();
            var start = text.Length - text.TrimStart().Length;
            var end = text.TrimEnd().Length;
            if (end > start) {
                var startTime = ToSeconds(token.StartMs);
                var endTime = ToSeconds(token.EndMs);
                if (map.Length > 0 && (endTime < map[^1].Y
                    || (offset + start > map[^1].X && startTime < map[^1].Y)))
                    throw new ArgumentOutOfRangeException(nameof(tokens));

                map = TryAppend(map, offset + start, startTime);
                map = TryAppend(map, offset + end, endTime);
            }
            offset += text.Length;
        }
        return map;
    }

    public IReadOnlyList<Transcript> Update(IReadOnlyList<SonioxToken> tokens, long audioProcMs)
    {
        var stableEndMs = audioProcMs - _stableTokenAgeMs;
        var tail = new StringBuilder();
        var tailTimingTokens = new List<SonioxToken>();
        var map = _finalMap;
        var tailStartOffset = _finalText.Length;
        var endTime = _finalEndTime;
        var hasNewFinals = false;
        var isSegmentEnd = false;
        foreach (var token in tokens) {
            if (token.Text == EndpointToken) {
                isSegmentEnd = true;
                continue;
            }
            if (token.Text.IsNullOrEmpty())
                continue;

            if (token.StartMs < _promotedEndMs)
                continue;

            if (token.IsFinal || (tail.Length == 0 && token.EndMs <= stableEndMs)) {
                hasNewFinals = true;
                // The languages come from settled tokens only: a tail token's tag is retracted with
                // the tail, and a wrong one would decide the dub for the whole utterance
                AddLanguage(token.Language);
                // A final token can only arrive while the tail is still empty for this message:
                // Soniox emits finals before the non-final tail it supersedes.
                var startOffset = _finalText.Length;
                _finalText.Append(token.Text);
                _finalTimingTokens.Add(CopyToken(token));
                _finalMap = AppendToken(_finalMap, startOffset, token);
                _finalEndTime = ToSeconds(token.EndMs);
                if (!token.IsFinal)
                    _promotedEndMs = token.EndMs;
                map = _finalMap;
                tailStartOffset = _finalText.Length;
                endTime = _finalEndTime;
                continue;
            }

            var tailOffset = tailStartOffset + tail.Length;
            tail.Append(token.Text);
            tailTimingTokens.Add(CopyToken(token));
            map = AppendToken(map, tailOffset, token);
            endTime = ToSeconds(token.EndMs);
        }

        _tailText = tail.ToString();
        _tailTimingTokens = tailTimingTokens;
        _tailMap = map;
        _tailEndTime = endTime;
        var transcripts = new List<Transcript>(2);
        if (hasNewFinals || (isSegmentEnd && _finalText.Length > 0))
            transcripts.Add(NewTranscript(_finalText.ToString(), _finalMap, _finalEndTime, true, isSegmentEnd));
        if (tail.Length > 0)
            transcripts.Add(NewTranscript(_finalText + _tailText, map, endTime, false));
        return transcripts;
    }

    public Transcript Complete(bool hasFinished = true)
    {
        var useTail = !hasFinished && _tailText.Length > 0;
        var transcript = useTail
            ? NewTranscript(_finalText + _tailText, _tailMap, _tailEndTime, true)
            : NewTranscript(_finalText.ToString(), _finalMap, _finalEndTime, true);
        var tokens = useTail
            ? _finalTimingTokens.Concat(_tailTimingTokens).ToArray()
            : _finalTimingTokens.ToArray();
        try {
            var map = CreateDetailedTimeMap(tokens);
            if (map.IsDegenerate)
                return transcript;

            return transcript with { TimeMap = map };
        }
        catch (ArgumentOutOfRangeException) {
            return transcript;
        }
    }

    // Private methods

    private Transcript NewTranscript(
        string text,
        LinearMap map,
        float endTime,
        bool isStable,
        bool isSegmentEnd = false)
    {
        if (map.IsDegenerate && !text.IsNullOrEmpty())
            map = new LinearMap(new Vector2(0, 0), new Vector2(text.Length, endTime));

        return new Transcript(text, map, _languages.ToArray()) { IsStable = isStable, IsSegmentEnd = isSegmentEnd };
    }

    private void AddLanguage(string? code)
    {
        var language = Language.TryParse(code, true);
        if (language != null && !_languages.Contains(language))
            _languages.Add(language);
    }

    private static LinearMap AppendToken(LinearMap map, int startOffset, SonioxToken token)
    {
        var text = token.Text.AsSpan();
        var start = text.Length - text.TrimStart().Length;
        var end = text.TrimEnd().Length;
        return end <= start ? map : TryAppend(
            TryAppend(map, startOffset + start, ToSeconds(token.StartMs)),
            startOffset + end, ToSeconds(token.EndMs));
    }

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

    private static SonioxToken CopyToken(SonioxToken token)
        => new() {
            Text = token.Text,
            StartMs = token.StartMs,
            EndMs = token.EndMs,
        };

    private static float ToSeconds(long ms)
        => (float)(ms / 1000.0);
}
