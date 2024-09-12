namespace ActualChat.UI.Blazor.Diagnostics;

public static class AppUIOtelSetup
{
    public static void SetupConditionalPropagator()
    {
        if (DistributedContextPropagator.Current is ConditionalPropagator)
            return;

        DistributedContextPropagator.Current = new ConditionalPropagator();
    }
}
