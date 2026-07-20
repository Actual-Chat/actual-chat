namespace ActualChat.UI.Blazor.Components;

public static class MapTypes
{
    // TODO: enable Satellite/Hybrid once the tile source has satellite imagery (openfreemap.org doesn't)
    public static readonly (string Type, string Label, string Icon, bool IsSupported)[] All = [
        ("map", "Map", "icon-map", true),
        ("satellite", "Satellite", "icon-globe", false),
        ("hybrid", "Hybrid", "icon-grid", false),
    ];
}
