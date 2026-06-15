namespace ActualChat.UI.Blazor.Services;

public class VisualMediaViewerUI(UIHub hub)
{
    private ModalUI ModalUI => hub.ModalUI;

    public async Task Show(IMediaCollectionView collection)
    {
        var model = new VisualMediaViewerModal.Model(collection);
        var modalRef = await ModalUI.Show(model).ConfigureAwait(false);
        await modalRef.WhenClosed.ConfigureAwait(false);
    }

    public Task Show(string url, ChatEntryAttachment[] attachments)
    {
        var index = Array.FindIndex(attachments,
            a => string.Equals(hub.UrlMapper.ContentUrl(a.Media.BlobId), url, StringComparison.OrdinalIgnoreCase));
        return Show(new FixedMediaCollectionView(attachments, Math.Max(0, index)));
    }
}
