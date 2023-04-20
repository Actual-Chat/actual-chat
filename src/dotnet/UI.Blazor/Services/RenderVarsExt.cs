namespace ActualChat.UI.Blazor.Services;

public static class RenderVarsExt
{
    public static RenderVar<List<RenderIntoSlot>> RenderSlot(this RenderVars renderVars, string name)
        => renderVars.Get($"Slot {name}", new List<RenderIntoSlot>());

    public static RenderVar<List<RenderIntoStack>> RenderStack(this RenderVars renderVars, string name)
        => renderVars.Get($"Stack {name}", new List<RenderIntoStack>());
}
