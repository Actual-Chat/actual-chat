namespace ActualChat.UI.Blazor.Services;

public sealed class ShareUI(UIHub hub) : UIServiceBase<UIHub>(hub)
{
    private IMauiShare? MauiShare { get; } = hub.Services.GetService<IMauiShare>();
    private WebShareInfo WebShareInfo => field ??= Services.GetRequiredService<WebShareInfo>();

    public Task<ModalRef> Share(ShareModalModel model)
        => ModalUI.Show(model);
    public Task<ModalRef> Share(ShareKind kind, string title, string targetTitle, ShareRequest request)
        => ModalUI.Show(new ShareModalModel(kind, title, targetTitle, request, null));

    public async ValueTask<bool> CanShareExternally()
        => MauiShare is not null || await WebShareInfo.CanShare().ConfigureAwait(false);
}
