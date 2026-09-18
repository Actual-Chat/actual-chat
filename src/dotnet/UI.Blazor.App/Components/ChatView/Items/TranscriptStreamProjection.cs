using ActualChat.Transcription;

namespace ActualChat.UI.Blazor.App.Components;

// The display side of a transcript stream: which part of the text stays, which part changed and
// which part is animated in, plus the not-yet-translated tail of a translation.
public sealed class TranscriptStreamProjection(string content, bool isTranslation)
{
    private readonly int _lastWordIndex = isTranslation ? content.LastIndexOf(' ') + 1 : 0;
    private string _lastText = "";
    private string _shownText = "";
    private int _stablePrefixLength;

    // Null: nothing changed on screen
    public TranscriptStreamReaderState? Next(Transcript transcript)
    {
        var text = transcript.Text;
        _lastText = text;
        var isShownPrefix = text.Length < _shownText.Length && _shownText.StartsWith(text, StringComparison.Ordinal);
        if (transcript.IsStable && isShownPrefix) {
            // A stable prefix of the shown text: the transcriber settled the head and re-sends
            // the tail next - erasing it now would just retype it. Complete() drops it for real.
            _stablePrefixLength = Math.Max(_stablePrefixLength, text.Length);
            return null;
        }

        var retainedLength = GetRetainedLength(_shownText, text, _stablePrefixLength);
        var changedPart = text[retainedLength..];

        // Animate only the delta growth; if it shrinks or equal, no animation
        var animatedLength = (text.Length - _shownText.Length).Clamp(0, changedPart.Length);
        var animatedStartIndex = changedPart.Length - animatedLength;

        var tail = "";
        if (isTranslation) {
            var tailStartIndex = text.Length.Clamp(0, _lastWordIndex);
            tail = content[tailStartIndex..];
        }

        if (transcript.IsStable)
            _stablePrefixLength = Math.Max(_stablePrefixLength, text.Length);
        _shownText = text;
        return new(
            RetainedText: text[..retainedLength],
            ChangedText: changedPart[..animatedStartIndex],
            AnimatedText: changedPart[animatedStartIndex..],
            Tail: tail,
            true,
            isTranslation);
    }

    public TranscriptStreamReaderState Complete()
        => new(
            RetainedText: _lastText,
            ChangedText: "",
            AnimatedText: "",
            Tail: "",
            false,
            isTranslation);

    // Private methods

    private static int GetRetainedLength(string previous, string current, int stablePrefixLength)
    {
        // Uses knowledge of an immutable prefix (stablePrefixLength) to avoid re-comparing it.
        // Also handles fast-paths for pure appends/truncations within the unstable suffix.
        if (previous.Length == 0 || current.Length == 0)
            return 0;

        var baseLen = Math.Min(stablePrefixLength, Math.Min(previous.Length, current.Length));

        // Fast append: current = previous + delta (beyond baseLen)
        if (previous.Length <= current.Length) {
            var prevSuffix = previous.AsSpan(baseLen);
            var currSuffix = current.AsSpan(baseLen);
            if (currSuffix.StartsWith(prevSuffix))
                return previous.Length;
        }

        // Fast truncate: previous = current + removed tail (beyond baseLen)
        if (current.Length <= previous.Length) {
            var currSuffix = current.AsSpan(baseLen);
            var prevSuffix = previous.AsSpan(baseLen);
            if (prevSuffix.StartsWith(currSuffix))
                return current.Length;
        }

        // Generic suffix common prefix scan after the stable base
        var a = previous.AsSpan(baseLen);
        var b = current.AsSpan(baseLen);
        var n = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < n && a[i] == b[i])
            i++;

        return baseLen + i;
    }
}
