
namespace ActualChat.UI.Blazor.Services;

public interface IClipboardHandlers
{
    [JSInvokable]
    Task WriteText(string? text);

    [JSInvokable]
    Task WriteRichText(string? text, string? html);

    [JSInvokable]
    Task<string?> ReadText();
}
