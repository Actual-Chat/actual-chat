namespace ActualChat.Users;

public sealed record CloseFlowInfo(
    string Name,
    string? RedirectUrl,
    bool MustClose);
