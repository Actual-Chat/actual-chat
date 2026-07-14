namespace ActualChat.UI.Blazor.Components;

public sealed record MapMarker(
    string Id,
    GeoPoint Point,
    string? Label = null,
    string? AvatarUrl = null,
    string? AvatarKey = null,
    bool IsOwnLocation = false);
