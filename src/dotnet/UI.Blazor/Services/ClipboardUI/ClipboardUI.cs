using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class ClipboardUI(UIHub hub) : UIServiceBase<UIHub>(hub)
{
    private static readonly string JSSelectAndGetMethod = $"{BlazorUICoreModule.ImportName}.selectAndGet";
    private static readonly string JSWriteRichMethod = $"{BlazorUICoreModule.ImportName}.writeRich";

    public ValueTask<string> ReadText()
        => JS.InvokeAsync<string>("navigator.clipboard.readText");

    public ValueTask WriteText(string text)
        => JS.InvokeVoidAsync("navigator.clipboard.writeText", text);

    public ValueTask WriteText(string text, string? html)
        => html.IsNullOrEmpty()
            ? WriteText(text)
            : JS.InvokeVoidAsync(JSWriteRichMethod, text, html);

    private ValueTask<string> GetTextFrom(ElementReference inputRef)
        => JS.InvokeAsync<string>(JSSelectAndGetMethod, inputRef);
}
