namespace ActualChat.UI.Blazor.Components;

public sealed record MapMarker(
    string Id,
    double Latitude,
    double Longitude,
    string? Label = null,
    string? Color = null);
