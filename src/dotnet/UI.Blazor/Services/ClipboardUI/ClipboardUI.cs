using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class ClipboardUI
{
    private readonly IJSRuntime _js;

    public ClipboardUI(IJSRuntime js)
        => _js = js;

    public virtual ValueTask<string> ReadText()
        => _js.InvokeAsync<string>("navigator.clipboard.readText");

    public virtual ValueTask WriteText(string text)
        => _js.InvokeVoidAsync("navigator.clipboard.writeText", text);

    protected virtual ValueTask<string> GetTextFrom(ElementReference inputRef)
        => _js.InvokeAsync<string>($"{BlazorUICoreModule.ImportName}.selectAndGet", inputRef);
}
