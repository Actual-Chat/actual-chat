namespace ActualChat.UI.Blazor.Services;

public class ImagePreviewUI
{
    private readonly ModalUI _modalUI;

    public ImagePreviewUI(ModalUI modalUI)
        => _modalUI = modalUI;

    public Task Show(string url, string? altText = null, string? cachedImageUrl = null, int width = 0, int height = 0)
        => _modalUI.Show(new ImagePreviewModal.Model(url, cachedImageUrl, altText, width, height)).Result;
}
