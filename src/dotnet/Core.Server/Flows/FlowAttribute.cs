namespace ActualChat.Flows;

[AttributeUsage(AttributeTargets.Class)]
public sealed class FlowAttribute : Attribute
{
    public double ResumeTimeout { get; set; } = double.NaN; // NaN means default
    public double DelayQuanta { get; set; } = double.NaN; // NaN means "use AutoDelayQuanta.For(delay)"
    public int DataVersion { get; set; } = 1;

    public TimeSpan? GetResumeTimeoutAsTimeSpan()
        => double.IsNaN(ResumeTimeout) ? null : TimeSpan.FromSeconds(ResumeTimeout).Positive();

    public TimeSpan? GetDelayQuantaAsTimeSpan()
        => double.IsNaN(DelayQuanta) ? null : TimeSpan.FromSeconds(DelayQuanta).Positive();
}
