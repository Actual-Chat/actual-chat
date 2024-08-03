namespace ActualChat.Diagnostics;

public interface IHealthState
{
    IState<double> CpuMean { get; }
    IState<double> CpuMean5 { get; }
    IState<double> CpuMean20 { get; }
}
