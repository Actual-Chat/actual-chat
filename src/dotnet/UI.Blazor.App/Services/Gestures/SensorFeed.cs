namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Source of timestamped accelerometer and proximity readings.
/// The base implementation is a no-op: there are no sensors on the web.
/// </summary>
public class SensorFeed
{
    public event Action<SensorSample>? SampleReceived;
    public event Action<bool>? ProximityChanged;

    public virtual bool IsAccelerometerAvailable => false;
    public virtual bool IsProximityAvailable => false;

    public virtual void StartAccelerometer()
    { }

    public virtual void StopAccelerometer()
    { }

    public virtual void StartProximity()
    { }

    public virtual void StopProximity()
    { }

    protected void OnSample(SensorSample sample)
        => SampleReceived?.Invoke(sample);

    protected void OnProximityChanged(bool isCovered)
        => ProximityChanged?.Invoke(isCovered);
}
