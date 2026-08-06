namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// A position fix: a <see cref="GeoPoint"/> plus the moment the platform measured it. <see cref="At"/> comes
/// from the platform, not from when we received it - "last known position" APIs hand back old fixes instantly.
/// </summary>
public sealed record GeoFix(GeoPoint Point, Moment At);
