namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// One gyroscope reading in rad/s, device frame (Z is the screen normal), with the Z rotation the
/// feed integrated since its previous emitted reading - so a decimated stream loses no angle.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct GyroSample(Moment At, float RateX, float RateY, float RateZ, float DeltaZ);
