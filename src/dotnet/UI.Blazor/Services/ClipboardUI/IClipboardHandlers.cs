using Microsoft.JSInterop;

namespace ActualChat.UI.Blazor.Services;

public interface IClipboardHandlers
{
    [JSInvokable]
    Task WriteText(string? text);

    [JSInvokable]
    Task<string?> ReadText();
}
