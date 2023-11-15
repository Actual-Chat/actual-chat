using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class ClipboardUI(IJSRuntime js)
{
    private static readonly string JSSelectAndGetMethod = $"{BlazorUICoreModule.ImportName}.selectAndGet";

    private IJSRuntime JS { get; } = js;

    public virtual ValueTask<string> ReadText()
        => JS.InvokeAsync<string>("navigator.clipboard.readText");

    public virtual ValueTask WriteText(string text)
        => JS.InvokeVoidAsync("navigator.clipboard.writeText", text);

    protected virtual ValueTask<string> GetTextFrom(ElementReference inputRef)
        => JS.InvokeAsync<string>(JSSelectAndGetMethod, inputRef);
}
