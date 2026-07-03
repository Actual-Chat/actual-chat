using ActualChat.Hosting;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record ThermalCapConfig(
    // Escalation applies instantly; relaxing waits until the device has held
    // the lower thermal level this long — re-heating is much faster than
    // cooling, so an eager step-up just re-trips the throttle.
    double RecoveryDelaySeconds = 15,
    int SeriousMaxFps = 15,
    int CriticalMaxFps = 10)
{
    public static ThermalCapConfig Default { get; } = new();
}

/// <summary>
/// Thermal-pressure cap: converts <see cref="ThermalLevel"/> into hard limits
/// on outbound layers/fps and an inbound budget multiplier, stepping in BEFORE
/// the OS throttles the whole device.
/// </summary>
public sealed class ThermalCap
{
    private readonly int _deviceCameraCap;
    private readonly int _deviceScreencastCap;
    private readonly ThermalCapConfig _config;
    private ThermalLevel _effectiveLevel;
    private ThermalLevel _pendingLevel;
    private Moment? _pendingSince;

    public ThermalLevel Level => _effectiveLevel;
    public int CameraLayers => _effectiveLevel switch {
        ThermalLevel.Serious => Math.Max(1, _deviceCameraCap - 1),
        ThermalLevel.Critical => 1,
        _ => _deviceCameraCap,
    };
    public int ScreencastLayers => _effectiveLevel switch {
        ThermalLevel.Critical => 1,
        _ => _deviceScreencastCap,
    };
    // 0 = no ceiling (matches the recorder's setFpsCeiling contract).
    public int MaxFps => _effectiveLevel switch {
        ThermalLevel.Serious => _config.SeriousMaxFps,
        ThermalLevel.Critical => _config.CriticalMaxFps,
        _ => 0,
    };
    public double InboundBudgetMultiplier => _effectiveLevel switch {
        ThermalLevel.Serious => 0.5,
        ThermalLevel.Critical => 0.25,
        _ => 1.0,
    };
    public int MaxPlaybackLayerCount => _effectiveLevel switch {
        ThermalLevel.Serious => 2,
        ThermalLevel.Critical => 1,
        _ => int.MaxValue,
    };

    public ThermalCap(int deviceCameraCap, int deviceScreencastCap, ThermalCapConfig config)
    {
        _deviceCameraCap = deviceCameraCap;
        _deviceScreencastCap = deviceScreencastCap;
        _config = config;
    }

    public bool Tick(Moment now, ThermalLevel level)
    {
        if (level >= _effectiveLevel) {
            var changed = level != _effectiveLevel;
            _effectiveLevel = level;
            _pendingSince = null;
            return changed;
        }

        if (_pendingLevel != level || _pendingSince is null) {
            _pendingLevel = level;
            _pendingSince = now;
            return false;
        }
        if ((now - _pendingSince.Value).TotalSeconds < _config.RecoveryDelaySeconds)
            return false;

        _effectiveLevel = level;
        _pendingSince = null;
        return true;
    }
}
