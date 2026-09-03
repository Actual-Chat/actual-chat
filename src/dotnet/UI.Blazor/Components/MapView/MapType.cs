using ActualChat.Localization;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.Components;

public enum MapType { Map = 0, Satellite, Hybrid }

public sealed record MapTypeInfo(MapType Type, string Icon, bool IsSupported)
{
    public string GetLabel(IStringLocalizer l)
        => Type switch {
            MapType.Satellite => l.Location_MapTypeSatellite,
            MapType.Hybrid => l.Location_MapTypeHybrid,
            _ => l.Location_MapTypeMap,
        };
}

public static class MapTypes
{
    // TODO: enable Satellite/Hybrid once the tile source has satellite imagery (openfreemap.org doesn't)
    public static readonly MapTypeInfo[] All = [
        new(MapType.Map, "icon-map", true),
        new(MapType.Satellite, "icon-globe", false),
        new(MapType.Hybrid, "icon-grid", false),
    ];
}
