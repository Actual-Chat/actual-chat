namespace ActualChat.UI.Blazor.App.Services;

public sealed record ReplayState(
    ChatId ChatId,
    Moment StartAt,
    TimeSpan Offset = default,
    double Speed = 1.0,
    Moment? PausedAt = null);
