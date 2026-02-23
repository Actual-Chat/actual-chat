using ActualChat.UI.Blazor.Services;
using Android.Content;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui;

public class AndroidClipboardHandlers : IClipboardHandlers
{
    [JSInvokable]
    public Task WriteText(string? text)
        => DispatchToMainThread(() => {
            var clipboard = (ClipboardManager)Platform.AppContext.GetSystemService(Context.ClipboardService)!;
            var clip = ClipData.NewPlainText("text", text ?? "");
            clipboard.PrimaryClip = clip;
        });

    [JSInvokable]
    public Task<string?> ReadText()
        => DispatchToMainThread(() => {
            var clipboard = (ClipboardManager)Platform.AppContext.GetSystemService(Context.ClipboardService)!;
            return clipboard.PrimaryClip?.GetItemAt(0)?.Text;
        });
}
