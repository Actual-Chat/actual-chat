using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class JavaScriptAppSettings
{
    private IServiceProvider Services { get; }

    public JavaScriptAppSettings(IServiceProvider services)
        => Services = services;

    public Task Initialize(List<object?>? initCalls = null)
    {
        var jsMethod = $"{BlazorUICoreModule.ImportName}.AppSettings.init";
        var hostInfo = Services.GetRequiredService<HostInfo>();
        var session = Services.GetRequiredService<Session>();
        if (initCalls != null) {
            initCalls.Add(jsMethod);
            initCalls.Add(2);
            initCalls.Add(hostInfo.BaseUrl);
            initCalls.Add(session.Hash);
            return Task.CompletedTask;
        }

        var js = Services.GetRequiredService<IJSRuntime>();
        return js.InvokeVoidAsync(jsMethod, hostInfo.BaseUrl, session.Hash).AsTask();
    }
}
