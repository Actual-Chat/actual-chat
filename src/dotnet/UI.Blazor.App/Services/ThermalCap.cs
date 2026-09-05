
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
public sealed class ThermalCap(
    int deviceCameraCap,
    int deviceScreencastCap,
    ThermalCapConfig config)
{
    private readonly Lock _lock = new();
    private volatile ThermalLevel _effectiveLevel;
    private ThermalLevel _pendingLevel;
    private Moment? _pendingSince;

    public ThermalLevel Level => _effectiveLevel;
    public int CameraLayers {
        get => _effectiveLevel switch {
            ThermalLevel.Serious => Math.Max(1, field - 1),
            ThermalLevel.Critical => 1,
            _ => field,
        };
    } = deviceCameraCap;

    public int ScreencastLayers {
        get => _effectiveLevel switch {
            ThermalLevel.Critical => 1,
            _ => field,
        };
    } = deviceScreencastCap;

    // 0 = no ceiling (matches the recorder's setFpsCeiling contract).
    public int MaxFps => _effectiveLevel switch {
        ThermalLevel.Serious => config.SeriousMaxFps,
        ThermalLevel.Critical => config.CriticalMaxFps,
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

    public bool Tick(Moment now, ThermalLevel level)
    {
        // Called from three independent async chains (WatchThermal, the outbound
        // QC tick, playback recompute); serialize the compound read-modify-write
        // so a cooldown reset and an escalation can't interleave and drop/flap.
        lock (_lock) {
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
            if ((now - _pendingSince.Value).TotalSeconds < config.RecoveryDelaySeconds)
                return false;

            _effectiveLevel = level;
            _pendingSince = null;
            return true;
        }
    }
}
