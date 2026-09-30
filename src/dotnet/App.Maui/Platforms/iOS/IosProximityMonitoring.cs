namespace ActualChat.App.Maui;

// UIDevice.ProximityMonitoringEnabled is one process-wide switch with two users - the gesture
// sensors and an earpiece call - so either one turning it off must not pull it from the other.
// Main thread only: UIKit requires it, and it's also what keeps the count unsynchronized.
public static class IosProximityMonitoring
{
    private static int _holderCount;

    public static void Acquire()
    {
        if (++_holderCount == 1)
            UIKit.UIDevice.CurrentDevice.ProximityMonitoringEnabled = true;
    }

    public static void Release()
    {
        if (_holderCount == 0)
            return;

        if (--_holderCount == 0)
            UIKit.UIDevice.CurrentDevice.ProximityMonitoringEnabled = false;
    }
}
