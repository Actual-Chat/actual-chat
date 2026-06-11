namespace ActualChat.UI.Blazor.Services;

public class VisualMediaViewerUI(UIHub hub)
{
    private ModalUI ModalUI => hub.ModalUI;

    public async Task Show(string url, ChatEntryAttachment[] attachments)
    {
        var model = new VisualMediaViewerModal.Model(url, attachments);
        var modalRef = await ModalUI.Show(model).ConfigureAwait(false);
        await modalRef.WhenClosed.ConfigureAwait(false);
    }

    public async Task Show(string url, GalleryContext gallery)
    {
        var model = new VisualMediaViewerModal.Model(url, [], gallery);
        var modalRef = await ModalUI.Show(model).ConfigureAwait(false);
        await modalRef.WhenClosed.ConfigureAwait(false);
    }
}
