namespace ActualChat.UI.Blazor.Components;

public enum MapType { Map = 0, Satellite, Hybrid }

public sealed record MapTypeInfo(MapType Type, string Label, string Icon, bool IsSupported);

public static class MapTypes
{
    // TODO: enable Satellite/Hybrid once the tile source has satellite imagery (openfreemap.org doesn't)
    public static readonly MapTypeInfo[] All = [
        new(MapType.Map, "Map", "icon-map", true),
        new(MapType.Satellite, "Satellite", "icon-globe", false),
        new(MapType.Hybrid, "Hybrid", "icon-grid", false),
    ];
}
