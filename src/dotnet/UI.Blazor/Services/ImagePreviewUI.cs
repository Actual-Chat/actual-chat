namespace ActualChat.UI.Blazor.Services;

public class ImagePreviewUI
{
    private readonly ModalUI _modalUI;

    public ImagePreviewUI(ModalUI modalUI)
        => _modalUI = modalUI;

    public Task Show(string url, string? altText = null, string? cachedImageUrl = null, int? width = null, int? height = null)
        => _modalUI.Show(new ImagePreviewModal.Model(url, cachedImageUrl, altText, width, height)).WhenClosed;
}
