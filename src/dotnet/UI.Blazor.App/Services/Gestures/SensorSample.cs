namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// One accelerometer reading in g units. MAUI normalizes axes across platforms;
/// a device lying flat face-up reads Z ≈ +1.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SensorSample(Moment At, float X, float Y, float Z)
{
    public float Magnitude => MathF.Sqrt((X * X) + (Y * Y) + (Z * Z));

    public GravityAxis GetDominantAxis(float minDominance)
    {
        var (ax, ay, az) = (MathF.Abs(X), MathF.Abs(Y), MathF.Abs(Z));
        if (ax >= minDominance && ax > ay && ax > az)
            return GravityAxis.X;
        if (ay >= minDominance && ay > ax && ay > az)
            return GravityAxis.Y;
        if (az >= minDominance && az > ax && az > ay)
            return GravityAxis.Z;

        return GravityAxis.None;
    }
}

public enum GravityAxis
{
    None = 0,
    X,
    Y,
    Z,
}
