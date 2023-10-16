namespace ActualChat.Diagnostics;

public interface IRuntimeStats
{
    IState<double> CpuMean { get; }
    IState<double> CpuMean5 { get; }
    IState<double> CpuMean20 { get; }
}
