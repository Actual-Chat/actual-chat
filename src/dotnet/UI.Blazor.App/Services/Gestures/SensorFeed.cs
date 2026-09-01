namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Source of timestamped accelerometer and proximity readings.
/// The base implementation is a no-op: there are no sensors on the web.
/// </summary>
public class SensorFeed
{
    public event Action<SensorSample>? SampleReceived;
    public event Action<bool>? ProximityChanged;

    public static bool IsAccelerometerStale(
        Moment startedAt, Moment lastSampleAt, Moment now, TimeSpan timeout)
        // Delivery, not the platform's own "started" flag: one process-wide accelerometer is
        // shared by every scope, so a registration can die under a feed that still believes it
        // owns one - and nothing would restart it. No sample yet dates from the start instead,
        // which is what keeps a warming-up sensor from reading as dead.
        => now - (lastSampleAt != default ? lastSampleAt : startedAt) >= timeout;

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
