namespace ActualChat.UI.Blazor.Services;

public static class RenderVarsExt
{
    public static IMutableState<RenderIntoSlot?> RenderSlot(this RenderVars renderVars, string name)
        => renderVars.Get<RenderIntoSlot?>($"Slot:{name}");

    public static IMutableState<ImmutableList<RenderIntoStack>> RenderStack(this RenderVars renderVars, string name)
        => renderVars.Get($"Stack:{name}", ImmutableList<RenderIntoStack>.Empty);
}
