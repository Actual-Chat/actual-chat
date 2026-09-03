using Microsoft.Extensions.Localization;

namespace ActualChat.Localization;

public static class DistanceLocalizerExt
{
    extension(IStringLocalizer l)
    {
        public string DistanceText(GeoPoint from, GeoPoint to)
            => l.DistanceText(from.DistanceTo(to));

        public string DistanceText(double meters)
            => meters switch {
                < 1 => l.Distance_Meters_Format(1),
                < 1000 => l.Distance_Meters_Format(meters.ToString("F0")),
                < 10_000 => l.Distance_Kilometers_Format((meters / 1000).ToString("F1")),
                _ => l.Distance_Kilometers_Format((meters / 1000).ToString("F0")),
            };
    }
}
