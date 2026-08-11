namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Sliding-window accumulator of how long remote authors have been speaking, used to tell
/// a real conversation from a mic someone simply left open.
/// </summary>
public sealed class RemoteSpeechWindow(TimeSpan window, TimeSpan threshold)
{
    private readonly Queue<(Moment Start, Moment End)> _spans = new();
    private Moment? _speakingSince;

    public bool IsSpeaking => _speakingSince.HasValue;

    public void Update(bool isSpeaking, Moment now)
    {
        if (isSpeaking) {
            _speakingSince ??= now;
            return;
        }
        if (_speakingSince is not { } since)
            return;

        _speakingSince = null;
        if (now > since)
            _spans.Enqueue((since, now));
    }

    public bool IsConversation(Moment now)
    {
        // Anyone audibly speaking right now already tells the user they're in a call
        if (_speakingSince.HasValue)
            return true;

        var from = now - window;
        while (_spans.TryPeek(out var oldest) && oldest.End <= from)
            _spans.Dequeue();

        var total = TimeSpan.Zero;
        foreach (var span in _spans) {
            total += span.End - (span.Start > from ? span.Start : from);
            if (total >= threshold)
                return true;
        }
        return false;
    }

    public void Reset()
    {
        _spans.Clear();
        _speakingSince = null;
    }
}
