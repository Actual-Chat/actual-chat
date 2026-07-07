using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class ClipboardUI(UIHub hub) : UIServiceBase<UIHub>(hub)
{
    private static readonly string JSSelectAndGetMethod = $"{BlazorUICoreModule.ImportName}.selectAndGet";
    private static readonly string JSWriteRichMethod = $"{BlazorUICoreModule.ImportName}.writeRich";

    public virtual bool CanWriteImage => false;

    public ValueTask<string> ReadText()
        => JS.InvokeAsync<string>("navigator.clipboard.readText", Hub.StopToken);

    public ValueTask WriteText(string text)
        => JS.InvokeVoidAsync("navigator.clipboard.writeText", Hub.StopToken, text);

    public ValueTask WriteText(string text, string? html)
        => html.IsNullOrEmpty()
            ? WriteText(text)
            : JS.InvokeVoidAsync(JSWriteRichMethod, Hub.StopToken, text, html);

    public virtual Task WriteImage(string uri)
        => Task.CompletedTask;

    private ValueTask<string> GetTextFrom(ElementReference inputRef)
        => JS.InvokeAsync<string>(JSSelectAndGetMethod, Hub.StopToken, inputRef);
}
