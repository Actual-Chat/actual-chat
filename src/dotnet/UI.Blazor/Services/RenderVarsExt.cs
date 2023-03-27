namespace ActualChat.UI.Blazor.Services;

public static class RenderVarsExt
{
    public static IMutableState<ImmutableList<RenderIntoSlot>> RenderSlot(this RenderVars renderVars, string name)
        => renderVars.Get($"Slot:{name}", ImmutableList<RenderIntoSlot>.Empty);

    public static IMutableState<ImmutableList<RenderIntoStack>> RenderStack(this RenderVars renderVars, string name)
        => renderVars.Get($"Stack:{name}", ImmutableList<RenderIntoStack>.Empty);
}
