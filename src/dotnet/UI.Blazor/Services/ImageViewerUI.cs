namespace ActualChat.UI.Blazor.Services;

public class ImageViewerUI
{
    private readonly ModalUI _modalUI;

    public ImageViewerUI(ModalUI modalUI)
        => _modalUI = modalUI;

    public Task Show(string url, string? altText = null, string? cachedImageUrl = null, int? width = null, int? height = null)
        => _modalUI.Show(new ImageViewerModal.Model(url, cachedImageUrl, altText, width, height)).WhenClosed;
}
