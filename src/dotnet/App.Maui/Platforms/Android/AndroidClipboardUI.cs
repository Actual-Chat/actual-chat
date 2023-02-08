using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui;

internal class AndroidClipboardUI : ClipboardUI
{
    public AndroidClipboardUI(IJSRuntime js) : base(js)
    {
    }

    public override async ValueTask<string> ReadText()
    {
        if (Clipboard.Default.HasText)
            return await Clipboard.Default.GetTextAsync().ConfigureAwait(true) ?? "";
        return "";
    }

    public override async ValueTask WriteText(string text)
        => await Clipboard.Default.SetTextAsync(text).ConfigureAwait(true);

    public override async ValueTask CopyTextFrom(ElementReference inputRef, string? text = null)
    {
        var text2 = await GetTextFrom(inputRef).ConfigureAwait(true);
        await WriteText(text ?? text2).ConfigureAwait(true);
    }
}
