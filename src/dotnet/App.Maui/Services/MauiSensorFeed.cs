using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.App.Maui.Services;

public sealed class MauiSensorFeed(AppUIHub hub) : SensorFeed
{
    // The four Start/Stop methods are called from both the GestureUI worker thread and the
    // Blazor UI thread (the platform main thread under BlazorWebView), and each is a
    // check-then-set on its flag - the lock keeps the flag and the hardware from disagreeing.
    // It's held across the MAUI sensor start/stop calls, which don't block on the main thread.
    // The flag is the intended state, so on iOS it's set here, not inside the dispatch below.
    // Accelerometer.Default is one process-wide resource, but this feed is scoped: a warm start
    // or a headless wake scope can leave two instances alive at once. Counting the holders is
    // what stops a dying scope's Stop() from killing the registration a live scope depends on -
    // its own flag would stay true, so nothing would ever start it again.
    private static int _accelerometerHolderCount;

    private readonly Lock _lock = new();
    private bool _isAccelerometerOn;
    private bool _isProximityOn;
    private Moment _lastSampleAt;
    private Moment _accelerometerStartedAt;

    private ILogger Log => field ??= hub.LogFor(GetType());

    public override bool IsAccelerometerAvailable => Accelerometer.Default.IsSupported;

    public override void StartAccelerometer()
    {
        lock (_lock) {
            if (!Accelerometer.Default.IsSupported)
                return;

            if (_isAccelerometerOn) {
                if (!IsAccelerometerStale(
                        _accelerometerStartedAt, _lastSampleAt, hub.Clocks.CpuClock.Now,
                        Constants.Audio.GestureSensorStaleTimeout))
                    return;

                // Re-asserted for every holder rather than re-counted: the shared sensor is as
                // dead for the others as it is here, and their flags say otherwise too.
                Log.LogWarning("The accelerometer stopped delivering - re-asserting it");
                TryStopShared();
                TryStartShared();
                _lastSampleAt = default;
                _accelerometerStartedAt = hub.Clocks.CpuClock.Now;
                return;
            }

            try {
                Accelerometer.Default.ReadingChanged += OnReadingChanged;
                if (Interlocked.Increment(ref _accelerometerHolderCount) == 1)
                    TryStartShared();
                _isAccelerometerOn = true;
                _lastSampleAt = default;
                _accelerometerStartedAt = hub.Clocks.CpuClock.Now;
            }
            catch (Exception e) {
                Accelerometer.Default.ReadingChanged -= OnReadingChanged;
                Interlocked.Decrement(ref _accelerometerHolderCount);
                Log.LogWarning(e, "Failed to start the accelerometer");
            }
        }
    }

    public override void StopAccelerometer()
    {
        lock (_lock) {
            if (!_isAccelerometerOn)
                return;

            _isAccelerometerOn = false;
            // Or a stale stamp would gate the first sample after the next start.
            _lastSampleAt = default;
            _accelerometerStartedAt = default;
            Accelerometer.Default.ReadingChanged -= OnReadingChanged;
            // Only the last holder stops the hardware - see _accelerometerHolderCount.
            if (Interlocked.Decrement(ref _accelerometerHolderCount) == 0)
                TryStopShared();
        }
    }

    // Private methods

    private void TryStartShared()
    {
        try {
            // SensorSpeed.UI ~= 60ms/sample; clears the ~166ms bound the 500ms-window
            // detectors need for a 4-6Hz shake.
            Accelerometer.Default.Start(SensorSpeed.UI);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to start the accelerometer");
        }
    }

    private void TryStopShared()
    {
        try {
            Accelerometer.Default.Stop();
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to stop the accelerometer");
        }
    }

    private void OnReadingChanged(object? sender, AccelerometerChangedEventArgs e)
    {
        // SensorSpeed.UI is only a hint: whenever another app pins the accelerometer at its max
        // rate the shared HAL rate rises and every connection gets the flood (470Hz measured on a
        // OnePlus CPH2747 against our 15Hz request), which would run three detectors and two locks
        // that often on the platform main thread.
        var now = hub.Clocks.CpuClock.Now;
        if (now - _lastSampleAt < Constants.Audio.GestureSampleMinPeriod)
            return;

        _lastSampleAt = now;
        var a = e.Reading.Acceleration;
        OnSample(new SensorSample(now, a.X, a.Y, a.Z));
    }

#if ANDROID
    private ProximityListener? _proximityListener;

    public override bool IsProximityAvailable
        => GetProximitySensor() is not null;

    public override void StartProximity()
    {
        lock (_lock) {
            if (_isProximityOn)
                return;

            var sensorManager = GetSensorManager();
            var sensor = GetProximitySensor();
            if (sensorManager is null || sensor is null)
                return;

            try {
                _proximityListener = new ProximityListener(sensor.MaximumRange, OnProximityChanged);
                sensorManager.RegisterListener(
                    _proximityListener, sensor, Android.Hardware.SensorDelay.Normal);
                _isProximityOn = true;
            }
            catch (Exception e) {
                _proximityListener = null;
                Log.LogWarning(e, "Failed to start proximity monitoring");
            }
        }
    }

    public override void StopProximity()
    {
        lock (_lock) {
            if (!_isProximityOn)
                return;

            _isProximityOn = false;
            try {
                if (_proximityListener is { } listener)
                    GetSensorManager()?.UnregisterListener(listener);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to stop proximity monitoring");
            }
            _proximityListener = null;
        }
        OnProximityChanged(false);
    }

    private static Android.Hardware.SensorManager? GetSensorManager()
        => Android.App.Application.Context.GetSystemService(Android.Content.Context.SensorService)
            as Android.Hardware.SensorManager;

    private static Android.Hardware.Sensor? GetProximitySensor()
        => GetSensorManager()?.GetDefaultSensor(Android.Hardware.SensorType.Proximity);

    private sealed class ProximityListener(float maxRange, Action<bool> onChange)
        : Java.Lang.Object, Android.Hardware.ISensorEventListener
    {
        public void OnAccuracyChanged(Android.Hardware.Sensor? sensor, Android.Hardware.SensorStatus accuracy)
        { }

        public void OnSensorChanged(Android.Hardware.SensorEvent? e)
        {
            if (e?.Values is not { Count: > 0 } values)
                return;

            onChange(values[0] < maxRange);
        }
    }
#elif IOS
    private Foundation.NSObject? _proximityObserver;

    public override bool IsProximityAvailable => true;

    public override void StartProximity()
    {
        lock (_lock) {
            if (_isProximityOn)
                return;

            _isProximityOn = true;
        }
        // UIKit is main-thread only; the main-thread queue is also what orders start vs. stop,
        // so _proximityObserver is touched from that thread alone.
        MainThread.BeginInvokeOnMainThread(() => {
            try {
                UIKit.UIDevice.CurrentDevice.ProximityMonitoringEnabled = true;
                _proximityObserver ??= Foundation.NSNotificationCenter.DefaultCenter.AddObserver(
                    UIKit.UIDevice.ProximityStateDidChangeNotification,
                    _ => OnProximityChanged(UIKit.UIDevice.CurrentDevice.ProximityState));
            }
            catch (Exception e) {
                // No rollback of the flag from here: it leaves proximity dead rather than live,
                // and GestureUI's next disarm clears the flag so the next arm retries.
                Log.LogWarning(e, "Failed to start proximity monitoring");
            }
        });
    }

    public override void StopProximity()
    {
        lock (_lock) {
            if (!_isProximityOn)
                return;

            _isProximityOn = false;
        }
        MainThread.BeginInvokeOnMainThread(() => {
            try {
                if (_proximityObserver is { } observer)
                    Foundation.NSNotificationCenter.DefaultCenter.RemoveObserver(observer);
                _proximityObserver = null;
                UIKit.UIDevice.CurrentDevice.ProximityMonitoringEnabled = false;
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to stop proximity monitoring");
            }
        });
        OnProximityChanged(false);
    }
#endif
}
