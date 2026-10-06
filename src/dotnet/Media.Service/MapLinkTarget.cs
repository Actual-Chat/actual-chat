namespace ActualChat.Media;

/// <summary>
/// The point a map link shows, with the name the link gives it; the name is empty when it gives none.
/// </summary>
public sealed record MapLinkTarget(GeoPoint Point, string Name = "");
