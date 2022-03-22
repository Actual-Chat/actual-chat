namespace ActualChat.UI.Blazor.Services;

public sealed class Clipboard
{
    private readonly IJSRuntime _js;

    public Clipboard(IJSRuntime js)
        => _js = js;

    public ValueTask<string> ReadText()
        => _js.InvokeAsync<string>("navigator.clipboard.readText");

    public ValueTask WriteText(string text)
        => _js.InvokeVoidAsync("navigator.clipboard.writeText", text);
}
