using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Services.Gestures;
using Microsoft.Maui.Devices.Sensors;

namespace ActualChat.App.Maui.Services;

public sealed class MauiSensorFeed(AppUIHub hub) : SensorFeed
{
    private bool _isAccelerometerOn;
    private bool _isProximityOn;

    private ILogger Log => field ??= hub.LogFor(GetType());

    public override bool IsAccelerometerAvailable => Accelerometer.Default.IsSupported;

    public override void StartAccelerometer()
    {
        if (_isAccelerometerOn || !Accelerometer.Default.IsSupported)
            return;

        try {
            Accelerometer.Default.ReadingChanged += OnReadingChanged;
            // SensorSpeed.UI ~= 60ms/sample; clears the ~166ms bound the 500ms-window detectors need for a 4-6Hz shake.
            Accelerometer.Default.Start(SensorSpeed.UI);
            _isAccelerometerOn = true;
        }
        catch (Exception e) {
            Accelerometer.Default.ReadingChanged -= OnReadingChanged;
            Log.LogWarning(e, "Failed to start the accelerometer");
        }
    }

    public override void StopAccelerometer()
    {
        if (!_isAccelerometerOn)
            return;

        _isAccelerometerOn = false;
        Accelerometer.Default.ReadingChanged -= OnReadingChanged;
        try {
            Accelerometer.Default.Stop();
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to stop the accelerometer");
        }
    }

    private void OnReadingChanged(object? sender, AccelerometerChangedEventArgs e)
    {
        var a = e.Reading.Acceleration;
        OnSample(new SensorSample(hub.Clocks.CpuClock.Now, a.X, a.Y, a.Z));
    }

#if ANDROID
    private ProximityListener? _proximityListener;

    public override bool IsProximityAvailable
        => GetProximitySensor() is not null;

    public override void StartProximity()
    {
        if (_isProximityOn)
            return;

        var sensorManager = GetSensorManager();
        var sensor = GetProximitySensor();
        if (sensorManager is null || sensor is null)
            return;

        _proximityListener = new ProximityListener(sensor.MaximumRange, OnProximityChanged);
        sensorManager.RegisterListener(
            _proximityListener, sensor, Android.Hardware.SensorDelay.Normal);
        _isProximityOn = true;
    }

    public override void StopProximity()
    {
        if (!_isProximityOn)
            return;

        _isProximityOn = false;
        if (_proximityListener is { } listener)
            GetSensorManager()?.UnregisterListener(listener);
        _proximityListener = null;
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
        if (_isProximityOn)
            return;

        UIKit.UIDevice.CurrentDevice.ProximityMonitoringEnabled = true;
        _proximityObserver = Foundation.NSNotificationCenter.DefaultCenter.AddObserver(
            UIKit.UIDevice.ProximityStateDidChangeNotification,
            _ => OnProximityChanged(UIKit.UIDevice.CurrentDevice.ProximityState));
        _isProximityOn = true;
    }

    public override void StopProximity()
    {
        if (!_isProximityOn)
            return;

        _isProximityOn = false;
        if (_proximityObserver is { } observer)
            Foundation.NSNotificationCenter.DefaultCenter.RemoveObserver(observer);
        _proximityObserver = null;
        UIKit.UIDevice.CurrentDevice.ProximityMonitoringEnabled = false;
        OnProximityChanged(false);
    }
#endif
}
