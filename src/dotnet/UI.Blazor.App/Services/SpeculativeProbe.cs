namespace ActualChat.UI.Blazor.App.Services;

public sealed record SpeculativeProbeConfig(
    // Ticks the +1 layer is held on the wire while we watch ack-age.
    int WindowTicks = 2,
    // The probe "fails" if ack-age rises by more than this over the baseline.
    double AckAgeInflationMs = 150,
    // Only start a probe when ack-age is at or below this (link looks idle/healthy).
    double HealthyAckAgeMs = 120,
    // Only start a probe when the wire queue is at or below this (no backlog).
    double ShallowQueueBundles = 1.5,
    int BaseCooldownTicks = 6,
    double CooldownGrowth = 1.7,
    int MaxCooldownTicks = 60)
{
    public static SpeculativeProbeConfig Default { get; } = new();
}

/// <summary>
/// Active uplink bandwidth probe for the idle case the drain-rate measurement
/// can't cover (an empty wire queue offers nothing to measure). Periodically
/// sends one extra spatial layer for a short window; if the uplink absorbs it
/// (ack-age stays flat), the bandwidth cap is allowed to climb — otherwise the
/// probe reverts and backs off. The committed climb only raises the health
/// ceiling; what's actually transmitted long-term is still gated by receiver
/// demand, so a passed probe costs nothing once the window ends.
/// </summary>
public sealed class SpeculativeProbe
{
    private readonly SpeculativeProbeConfig _config;
    private int _cooldownRemaining;
    private int _failures;
    private int _windowRemaining;
    private double _baselineAckAgeMs;

    public bool IsActive => _windowRemaining > 0;
    public SpeculativeProbeConfig Config => _config;

    public SpeculativeProbe(SpeculativeProbeConfig? config = null)
        => _config = config ?? SpeculativeProbeConfig.Default;

    // Drives the probe state machine once per outbound tick. Returns the extra
    // layer count (0 or 1) to add to the transmitted camera target. On a passed
    // probe it commits by bumping `cap` so subsequent ticks keep the headroom.
    public int Tick(
        bool canGrow,
        double ackAgeMs,
        double wireQueueDepth,
        LayerCap cap,
        IReadOnlyCollection<VideoSourceKind>? activeKinds = null)
    {
        if (_windowRemaining > 0) {
            _windowRemaining--;
            var inflated = (ackAgeMs >= 0 && ackAgeMs > _baselineAckAgeMs + _config.AckAgeInflationMs)
                || wireQueueDepth > _config.ShallowQueueBundles * 2;
            if (inflated) {
                // Link can't take the extra layer — abandon and back off.
                _windowRemaining = 0;
                _failures++;
                _cooldownRemaining = ComputeCooldown();
                return 0;
            }
            if (_windowRemaining == 0) {
                // Survived the whole window with flat ack-age → commit the climb.
                cap.Increase(activeKinds);
                _failures = 0;
                _cooldownRemaining = _config.BaseCooldownTicks;
                return 0;
            }
            return 1;
        }

        if (_cooldownRemaining > 0) {
            _cooldownRemaining--;
            return 0;
        }
        if (canGrow
            && ackAgeMs >= 0 && ackAgeMs <= _config.HealthyAckAgeMs
            && wireQueueDepth <= _config.ShallowQueueBundles) {
            _windowRemaining = _config.WindowTicks;
            _baselineAckAgeMs = Math.Max(0, ackAgeMs);
            return 1;
        }
        return 0;
    }

    public void Reset()
    {
        _windowRemaining = 0;
        _cooldownRemaining = 0;
        _failures = 0;
    }

    private int ComputeCooldown()
        => (int)Math.Min(
            _config.MaxCooldownTicks,
            _config.BaseCooldownTicks * Math.Pow(_config.CooldownGrowth, _failures));
}
