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
    private static int _gyroscopeHolderCount;

    private readonly Lock _lock = new();
    private bool _isAccelerometerOn;
#pragma warning disable CS0169 // Read only under ANDROID and IOS
    private bool _isProximityOn;
#pragma warning restore CS0169
    private Moment _lastSampleAt;
    private Moment _accelerometerStartedAt;
    private System.Numerics.Vector3 _droppedPeak;
    private float _droppedPeakDeviation = -1;
    private bool _isGyroscopeOn;
    private Moment _lastGyroAt;
    private Moment _lastGyroEmittedAt;
    private float _gyroDeltaZ;

    private ILogger Log => field ??= hub.LogFor(GetType());

    public override bool IsAccelerometerAvailable => Accelerometer.Default.IsSupported;

    public override void StartAccelerometer()
    {
        lock (_lock) {
            if (!Accelerometer.Default.IsSupported)
                return;

            if (_isAccelerometerOn) {
                // A gyroscope that failed to start is retried on every arm, not only after a stop
                StartGyroscopeUnguarded();
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
                StartGyroscopeUnguarded();
                _lastSampleAt = default;
                _droppedPeakDeviation = -1;
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
            StopGyroscopeUnguarded();
            // Only the last holder stops the hardware - see _accelerometerHolderCount.
            if (Interlocked.Decrement(ref _accelerometerHolderCount) == 0)
                TryStopShared();
        }
    }

    // Private methods

    private void TryStartShared()
    {
        try {
            // ~50Hz, what a pat needs (see GestureSampleMinPeriod). Game is 20ms on Android, but
            // only 40ms on iOS, where Fastest is the 20ms one.
            Accelerometer.Default.Start(OperatingSystem.IsAndroid() ? SensorSpeed.Game : SensorSpeed.Fastest);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to start the accelerometer");
        }
    }

    private void StartGyroscopeUnguarded()
    {
        // Flip-to-talk is a rotation, so it's read from the gyroscope, which sees rotation whatever
        // the hand does; it rides the accelerometer's lifecycle and holder counting.
        if (_isGyroscopeOn || !Gyroscope.Default.IsSupported)
            return;

        try {
            Gyroscope.Default.ReadingChanged += OnGyroReadingChanged;
            if (Interlocked.Increment(ref _gyroscopeHolderCount) == 1)
                Gyroscope.Default.Start(OperatingSystem.IsAndroid() ? SensorSpeed.Game : SensorSpeed.Fastest);
            _isGyroscopeOn = true;
            _lastGyroAt = default;
            _lastGyroEmittedAt = default;
            _gyroDeltaZ = 0f;
        }
        catch (Exception e) {
            Gyroscope.Default.ReadingChanged -= OnGyroReadingChanged;
            Interlocked.Decrement(ref _gyroscopeHolderCount);
            Log.LogWarning(e, "Failed to start the gyroscope");
        }
    }

    private void StopGyroscopeUnguarded()
    {
        if (!_isGyroscopeOn)
            return;

        _isGyroscopeOn = false;
        Gyroscope.Default.ReadingChanged -= OnGyroReadingChanged;
        if (Interlocked.Decrement(ref _gyroscopeHolderCount) != 0)
            return;

        try {
            Gyroscope.Default.Stop();
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to stop the gyroscope");
        }
    }

    private void OnGyroReadingChanged(object? sender, GyroscopeChangedEventArgs e)
    {
        // Every reading's rotation is integrated, so the decimated stream loses no angle
        var now = hub.Clocks.CpuClock.Now;
        var rate = e.Reading.AngularVelocity;
        if (_lastGyroAt != default)
            _gyroDeltaZ += rate.Z * (float)(now - _lastGyroAt).TotalSeconds;
        _lastGyroAt = now;
        if (now - _lastGyroEmittedAt < Constants.Audio.GestureSampleMinPeriod)
            return;

        _lastGyroEmittedAt = now;
        var deltaZ = _gyroDeltaZ;
        _gyroDeltaZ = 0f;
        OnGyro(new GyroSample(now, rate.X, rate.Y, rate.Z, deltaZ));
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
        // The requested speed is only a hint: whenever another app pins the accelerometer at its max
        // rate the shared HAL rate rises and every connection gets the flood (470Hz measured on a
        // OnePlus CPH2747; 120Hz on a SM-S948U1), which would run three detectors and two locks
        // that often on the platform main thread.
        var now = hub.Clocks.CpuClock.Now;
        var a = e.Reading.Acceleration;
        var deviation = MathF.Abs(a.Length() - 1f);
        if (now - _lastSampleAt < Constants.Audio.GestureSampleMinPeriod) {
            // A pat lasts 10-30ms, so the reading that carries it is often one the floor drops:
            // the strongest dropped reading stands in for the next emitted one if it's stronger.
            if (deviation > _droppedPeakDeviation) {
                _droppedPeak = a;
                _droppedPeakDeviation = deviation;
            }
            return;
        }

        if (_droppedPeakDeviation > deviation)
            a = _droppedPeak;
        _droppedPeakDeviation = -1;
        _lastSampleAt = now;
        OnSample(new SensorSample(now, a.X, a.Y, a.Z));
    }

#if ANDROID
    // A cover is confirmed by either sensor: the default proximity sensor may be one apps can't
    // really use - on Galaxy S-series it is Samsung's virtual "Palm Proximity", silent outside calls.
    private readonly LightCoverDetector _lightCover = new();
    private SensorListener? _proximityListener;
    private SensorListener? _lightListener;
    private bool _isProximityNear;
    private bool _isLightCovered;
    private bool _isCoverReported;
    private bool _isLightTickScheduled;
    private Android.OS.Handler? _lightTickHandler;

    public override bool IsProximityAvailable
        => GetSensor(Android.Hardware.SensorType.Proximity) is not null
            || GetSensor(Android.Hardware.SensorType.Light) is not null;

    public override void StartProximity()
    {
        lock (_lock) {
            if (_isProximityOn)
                return;

            var sensorManager = GetSensorManager();
            if (sensorManager is null)
                return;

            _isProximityNear = false;
            _isLightCovered = false;
            _isCoverReported = false;
            _lightCover.Reset();
            _proximityListener = TryRegister(sensorManager, Android.Hardware.SensorType.Proximity, OnProximityReading);
            _lightListener = TryRegister(sensorManager, Android.Hardware.SensorType.Light, OnLightReading);
            _isProximityOn = _proximityListener is not null || _lightListener is not null;
        }
    }

    public override void StopProximity()
    {
        lock (_lock) {
            if (!_isProximityOn)
                return;

            _isProximityOn = false;
            try {
                var sensorManager = GetSensorManager();
                if (_proximityListener is { } proximityListener)
                    sensorManager?.UnregisterListener(proximityListener);
                if (_lightListener is { } lightListener)
                    sensorManager?.UnregisterListener(lightListener);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to stop proximity monitoring");
            }
            _proximityListener = null;
            _lightListener = null;
            _lightCover.Reset();
            _isCoverReported = false;
            OnProximityChanged(false);
        }
    }

    private SensorListener? TryRegister(
        Android.Hardware.SensorManager sensorManager,
        Android.Hardware.SensorType sensorType,
        Action<Android.Hardware.SensorEvent> onReading)
    {
        var sensor = sensorManager.GetDefaultSensor(sensorType);
        if (sensor is null)
            return null;

        try {
            var listener = new SensorListener(onReading);
            sensorManager.RegisterListener(listener, sensor, Android.Hardware.SensorDelay.Normal);
            Log.LogInformation("Cover sensing uses {SensorType}: {SensorName}", sensorType, sensor.Name);
            return listener;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to start {SensorType} monitoring", sensorType);
            return null;
        }
    }

    private void OnProximityReading(Android.Hardware.SensorEvent e)
    {
        var isNear = e.Values![0] < (e.Sensor?.MaximumRange ?? 0f);
        UpdateCover(() => _isProximityNear = isNear);
    }

    private void OnLightReading(Android.Hardware.SensorEvent e)
    {
        var lux = e.Values![0];
        var now = hub.Clocks.CpuClock.Now;
        UpdateCover(() => _isLightCovered = _lightCover.Process(lux, now));
        ScheduleLightTick();
        // Raw, without the uncover delay: patting flickers a pocket to 2-4 lux, pulling it out passes 5
        if (lux > LightCoverDetector.PulledOutMinLux)
            OnLightRose();
    }

    private void ScheduleLightTick()
    {
        // The sensor reports on change only - a pocket at a steady 0 lux repeats every ~4s - so a
        // pending edge is re-checked on a timer rather than on the next reading.
        if (!_lightCover.IsPending || _isLightTickScheduled)
            return;

        _isLightTickScheduled = true;
        _lightTickHandler ??= new Android.OS.Handler(Android.OS.Looper.MainLooper!);
        _lightTickHandler.PostDelayed(() => {
            _isLightTickScheduled = false;
            if (!_isProximityOn)
                return; // Stopped meanwhile: nothing to re-check, so the timer ends here

            UpdateCover(() => _isLightCovered = _lightCover.Tick(hub.Clocks.CpuClock.Now));
            ScheduleLightTick();
        }, 250);
    }

    private void UpdateCover(Action update)
    {
        // Published under the lock, so a reading racing StopProximity can't land after its "uncovered"
        lock (_lock) {
            if (!_isProximityOn)
                return;

            update();
            var isCovered = _isProximityNear || _isLightCovered;
            if (isCovered == _isCoverReported)
                return;

            _isCoverReported = isCovered;
            OnProximityChanged(isCovered);
        }
    }

    private static Android.Hardware.SensorManager? GetSensorManager()
        => Android.App.Application.Context.GetSystemService(Android.Content.Context.SensorService)
            as Android.Hardware.SensorManager;

    private static Android.Hardware.Sensor? GetSensor(Android.Hardware.SensorType sensorType)
        => GetSensorManager()?.GetDefaultSensor(sensorType);

    private sealed class SensorListener(Action<Android.Hardware.SensorEvent> onReading)
        : Java.Lang.Object, Android.Hardware.ISensorEventListener
    {
        public void OnAccuracyChanged(Android.Hardware.Sensor? sensor, Android.Hardware.SensorStatus accuracy)
        { }

        public void OnSensorChanged(Android.Hardware.SensorEvent? e)
        {
            if (e?.Values is not { Count: > 0 })
                return;

            onReading(e);
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
        BeginDispatchToMainThread(() => {
            try {
                IosProximityMonitoring.Acquire();
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
        BeginDispatchToMainThread(() => {
            try {
                if (_proximityObserver is { } observer)
                    Foundation.NSNotificationCenter.DefaultCenter.RemoveObserver(observer);
                _proximityObserver = null;
                IosProximityMonitoring.Release();
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to stop proximity monitoring");
            }
        });
        OnProximityChanged(false);
    }
#endif
}
