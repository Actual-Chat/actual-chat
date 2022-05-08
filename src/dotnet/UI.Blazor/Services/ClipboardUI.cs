using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class ClipboardUI
{
    private readonly IJSRuntime _js;

    public ClipboardUI(IJSRuntime js)
        => _js = js;

    public ValueTask<string> ReadText()
        => _js.InvokeAsync<string>("navigator.clipboard.readText");

    public ValueTask WriteText(string text)
        => _js.InvokeVoidAsync("navigator.clipboard.writeText", text);

    public ValueTask CopyTextFrom(ElementReference inputRef, string? text = null)
        => _js.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.selectAndCopy", inputRef, text);
}
