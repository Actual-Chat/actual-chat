namespace ActualChat.Flows;

[AttributeUsage(AttributeTargets.Class)]
public sealed class FlowAttribute : Attribute
{
    public double ResumeTimeout { get; set; } = double.NaN; // In seconds, NaN means default
    public double DelayQuanta { get; set; } = double.NaN; // In seconds, NaN means "use AutoDelayQuanta.For(delay)"
    public int DataVersion { get; set; } = 1;
    // In days; NaN means the flow resumes only on external events, so nothing tells it is stuck
    public double MaxResumeGap { get; set; } = double.NaN;

    public TimeSpan? GetResumeTimeoutAsTimeSpan()
        => double.IsNaN(ResumeTimeout) ? null : TimeSpan.FromSeconds(ResumeTimeout).Positive();

    public TimeSpan? GetDelayQuantaAsTimeSpan()
        => double.IsNaN(DelayQuanta) ? null : TimeSpan.FromSeconds(DelayQuanta).Positive();

    public TimeSpan? GetMaxResumeGapAsTimeSpan()
        => double.IsNaN(MaxResumeGap) ? null : TimeSpan.FromDays(MaxResumeGap).Positive();
}
