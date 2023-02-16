using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class KeepAwakeUI
{
    private IJSRuntime JS { get; }

    public KeepAwakeUI(IServiceProvider services)
        => JS = services.GetRequiredService<IJSRuntime>();

    public ValueTask SetKeepAwake(bool value)
        => JS.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.KeepAwakeUI.setKeepAwake", value);
}
