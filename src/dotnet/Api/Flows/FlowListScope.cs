namespace ActualChat.Flows;

public enum FlowListScope
{
    Active = 0, // No result yet
    Ended, // Has a result, failed or not
    Problematic, // Failed or stuck
    All,
}
