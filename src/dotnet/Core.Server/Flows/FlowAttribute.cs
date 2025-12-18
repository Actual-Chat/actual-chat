namespace ActualChat.Flows;

[AttributeUsage(AttributeTargets.Class)]
public class FlowAttribute : Attribute
{
    public double ResumeTimeout { get; set; } = double.NaN; // NaN means default
    public int DataVersion { get; set; } = 1;

    public TimeSpan? GetResumeTimeoutAsTimeSpan()
        => double.IsNaN(ResumeTimeout) ? null : TimeSpan.FromSeconds(ResumeTimeout).Positive();
}
