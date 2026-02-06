namespace ActualChat.Diagnostics;

/// <summary>
/// Provides CPU usage metrics for health monitoring.
/// </summary>
public interface IHealthState
{
    IState<double> CpuMean { get; }
    IState<double> CpuMean5 { get; }
    IState<double> CpuMean20 { get; }
}
