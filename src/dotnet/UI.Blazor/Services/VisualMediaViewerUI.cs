using ActualChat.Chat;
namespace ActualChat.UI.Blazor.Services;

public class VisualMediaViewerUI(UIHub hub)
{
    private ModalUI ModalUI => hub.ModalUI;

    public async Task Show(string url, TextEntryAttachment[] attachments)
    {
        var model = new VisualMediaViewerModal.Model(url, attachments);
        var modalRef = await ModalUI.Show(model).ConfigureAwait(false);
        await modalRef.WhenClosed.ConfigureAwait(false);
    }
}
