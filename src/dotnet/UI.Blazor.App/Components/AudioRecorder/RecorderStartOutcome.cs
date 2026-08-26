namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// A <see cref="RecorderStartResult"/> plus the platform's own code for what we couldn't
/// classify - a DOMException name, an AudioGraph status, an AudioRecord state. It reaches the
/// user, so a support screenshot names the failure instead of needing a log dig.
/// </summary>
public readonly record struct RecorderStartOutcome(RecorderStartResult Result, string? Code = null)
{
    public static readonly RecorderStartOutcome Started = new(RecorderStartResult.Started);
    public bool IsStarted => Result == RecorderStartResult.Started;
    public static implicit operator RecorderStartOutcome(RecorderStartResult result) => new(result);
    public static RecorderStartOutcome Parse(string failure)
    {
        // The wire form every engine reports in: "" for started, else "<RecorderStartResult>:<code>"
        if (failure.IsNullOrEmpty())
            return Started;

        var separatorIndex = failure.IndexOf(':');
        var name = separatorIndex < 0 ? failure : failure[..separatorIndex];
        var code = separatorIndex < 0 ? null : failure[(separatorIndex + 1)..].NullIfEmpty();
        return Enum.TryParse<RecorderStartResult>(name, out var result) && result != RecorderStartResult.Started
            ? new RecorderStartOutcome(result, code)
            : new RecorderStartOutcome(RecorderStartResult.Unknown, failure);
    }
}
