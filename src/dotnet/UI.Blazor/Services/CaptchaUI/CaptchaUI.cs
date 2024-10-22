using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class CaptchaUI : ICaptchaUIBackend
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.{nameof(CaptchaUI)}.init";
    private static readonly string JSGetTokenMethod = $"{BlazorUICoreModule.ImportName}.{nameof(CaptchaUI)}.getToken";
    private readonly DotNetObjectReference<ICaptchaUIBackend> _blazorRef;
    private UIHub Hub { get; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SiteKey);
    public string SiteKey { get; private set; } = "";

    public CaptchaUI(UIHub hub)
    {
        Hub = hub;
        _blazorRef = DotNetObjectReference.Create<ICaptchaUIBackend>(this);
    }

    public void Initialize(string siteKey)
        => SiteKey = siteKey;

    public ValueTask EnsureInitialized()
        => IsConfigured ? ValueTask.CompletedTask : Initialize();

    public ValueTask<string> GetActionToken(string action, CancellationToken cancellationToken)
        => Hub.JSRuntime().InvokeAsync<string>(JSGetTokenMethod, CancellationToken.None, SiteKey, action);

    [JSInvokable]
    public void OnInitialized(string siteKey)
        => SiteKey = siteKey;

    private ValueTask Initialize()
        => Hub.JSRuntime().InvokeVoidAsync(JSInitMethod, _blazorRef);
}
