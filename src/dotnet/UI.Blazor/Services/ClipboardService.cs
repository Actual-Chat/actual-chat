using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace ActualChat.UI.Blazor.Services;

public sealed class ClipboardService
{
    private readonly IJSRuntime _jsRuntime;

    public ClipboardService(IJSRuntime jsRuntime)
        => _jsRuntime = jsRuntime;

    public ValueTask<string> ReadTextAsync()
        => _jsRuntime.InvokeAsync<string>("navigator.clipboard.readText");

    public ValueTask WriteTextAsync(string text)
        => _jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", text);
}
