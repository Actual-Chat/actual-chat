using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class CaptchaUI(UIHub hub) : UIServiceBase<UIHub>(hub)
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.{nameof(CaptchaUI)}.init";
    private static readonly string JSGetTokenMethod = $"{BlazorUICoreModule.ImportName}.{nameof(CaptchaUI)}.getToken";

    public string SiteKey { get; private set; } = "";
    public bool IsAvailable => !SiteKey.IsNullOrEmpty();
    public Task WhenReady => field ??= Initialize();

    public Task<string> GetActionToken(string action, CancellationToken cancellationToken)
        => Hub.JS
            .InvokeAsync<string>(JSGetTokenMethod, CancellationToken.None, SiteKey, action)
            .AsTask().WaitAsync(cancellationToken);

    private async Task<string> Initialize()
        => SiteKey = await Hub.JS.InvokeAsync<string>(JSInitMethod).ConfigureAwait(true);
}
