namespace ActualChat.UI.Blazor.Components;

public sealed record ShareModalModel(
    ShareKind Kind,
    string Title,
    string TargetTitle,
    ShareRequest Request);
