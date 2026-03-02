using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class ClipboardUI(UIHub hub) : UIServiceBase<UIHub>(hub)
{
    private static readonly string JSSelectAndGetMethod = $"{BlazorUICoreModule.ImportName}.selectAndGet";

    public ValueTask<string> ReadText()
        => JS.InvokeAsync<string>("navigator.clipboard.readText");

    public ValueTask WriteText(string text)
        => JS.InvokeVoidAsync("navigator.clipboard.writeText", text);

    private ValueTask<string> GetTextFrom(ElementReference inputRef)
        => JS.InvokeAsync<string>(JSSelectAndGetMethod, inputRef);
}
