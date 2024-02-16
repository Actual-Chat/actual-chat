namespace ActualChat.UI.Blazor.Services;

public sealed class ShareUI(UIHub hub) : ScopedServiceBase<UIHub>(hub)
{
    private ModalUI ModalUI => Hub.ModalUI;

    public Task<ModalRef> Share(ShareModalModel model)
        => ModalUI.Show(model);
    public Task<ModalRef> Share(ShareKind kind, string title, string targetTitle, ShareRequest request)
        => ModalUI.Show(new ShareModalModel(kind, title, targetTitle, request, null));
}
