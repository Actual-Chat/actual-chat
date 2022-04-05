namespace ActualChat.UI.Blazor.Services;

public static class RenderVarsExt
{
    public static IMutableState<RenderFragment?> RenderSlot(this RenderVars renderVars, string name)
        => renderVars.Get<RenderFragment?>($"Slot:{name}");
}
