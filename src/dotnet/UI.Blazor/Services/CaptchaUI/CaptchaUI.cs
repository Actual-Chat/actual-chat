using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class CaptchaUI(UIHub hub) : UIServiceBase<UIHub>(hub)
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.{nameof(CaptchaUI)}.init";
    private static readonly string JSGetTokenMethod = $"{BlazorUICoreModule.ImportName}.{nameof(CaptchaUI)}.getToken";
    private static readonly string JSSetFakeFailMethod = $"{BlazorUICoreModule.ImportName}.{nameof(CaptchaUI)}.setFakeCaptchaFail";

    public string SiteKey { get; private set; } = "";
    public bool IsAvailable => !SiteKey.IsNullOrEmpty();
    public bool IsFake => OrdinalEquals(SiteKey, "fake");
    public Task WhenReady => field ??= Initialize();

    public Task<string> GetActionToken(string action, CancellationToken cancellationToken)
        => JS.InvokeAsync<string>(JSGetTokenMethod, CancellationToken.None, SiteKey, action)
            .AsTask().WaitAsync(cancellationToken);

    public ValueTask SetFakeCaptchaFail(bool shouldFail)
        => JS.InvokeVoidAsync(JSSetFakeFailMethod, CancellationToken.None, shouldFail);

    // Private methods

    private async Task<string> Initialize()
        => SiteKey = await Hub.JS.InvokeAsync<string>(JSInitMethod).ConfigureAwait(true);
}
