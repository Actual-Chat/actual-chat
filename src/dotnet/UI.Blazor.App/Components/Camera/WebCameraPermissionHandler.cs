using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class WebCameraPermissionHandler : CameraPermissionHandler
{
    private static readonly string JSCheckPermission = $"{BlazorUIAppModule.ImportName}.{nameof(WebCameraPermissionHandler)}.checkPermission";
    private static readonly string JSRequestPermission = $"{BlazorUIAppModule.ImportName}.{nameof(WebCameraPermissionHandler)}.requestPermission";

    public WebCameraPermissionHandler(UIHub hub, bool mustStart = true) : base(hub, false)
    {
        ExpirationPeriod = null;
        if (mustStart)
            this.Start();
    }

    protected override async Task<bool?> Get(CancellationToken cancellationToken)
    {
        var permission = await JS.InvokeAsync<string?>(JSCheckPermission, cancellationToken).ConfigureAwait(false);
        return permission switch {
            "prompt" => null,
            "denied" => false,
            "granted" => true,
            _ => null,
        };
    }

    protected override async Task<bool> Request(CancellationToken cancellationToken)
        => await JS.InvokeAsync<bool>(JSRequestPermission, cancellationToken).ConfigureAwait(false);

    protected override Task Troubleshoot(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
