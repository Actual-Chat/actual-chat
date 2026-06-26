namespace ActualChat;

public enum GeoTrackingAccuracy
{
    High = 0,
    Balanced = 1,
    Low = 2,
}

public static class GeoTrackingAccuracyExt
{
    public static GeoTrackingAccuracy Next(this GeoTrackingAccuracy accuracy)
        => (GeoTrackingAccuracy)(((int)accuracy + 1) % 3);
}
