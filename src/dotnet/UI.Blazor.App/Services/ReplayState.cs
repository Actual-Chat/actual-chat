namespace ActualChat.UI.Blazor.App.Services;

public sealed record ReplayState(
    ChatId ChatId,
    Moment StartAt,
    TimeSpan RewindOffset = default,
    double Speed = 1.0,
    Moment? PausedAt = null);
