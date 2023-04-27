using ActualChat.Chat;
namespace ActualChat.UI.Blazor.Services;

public class ImageViewerUI
{
    private readonly ModalUI _modalUI;

    public ImageViewerUI(ModalUI modalUI)
        => _modalUI = modalUI;

    public async Task Show(
        string url,
        string? altText = null,
        string? cachedImageUrl = null,
        int? width = null,
        int? height = null,
        ChatEntry? chatEntry = null,
        bool isVideo = false)
    {
        var model = new ImageViewerModal.Model(url, cachedImageUrl, altText, width, height, chatEntry, isVideo);
        var modalRef = await _modalUI.Show(model);
        await modalRef.WhenClosed;
    }
}
