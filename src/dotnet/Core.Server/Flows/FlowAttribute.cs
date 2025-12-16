namespace ActualChat.Flows;

[AttributeUsage(AttributeTargets.Class)]
public class FlowAttribute : Attribute
{
    public int DataVersion { get; set; } = 1;
}
