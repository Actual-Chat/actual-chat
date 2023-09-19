using ActualChat.Chat;
namespace ActualChat.UI.Blazor.Services;

public class VisualMediaViewerUI(IServiceProvider services)
{
    private ModalUI ModalUI { get; } = services.GetRequiredService<ModalUI>();

    public async Task Show(
        string url,
        string? altText = null,
        string? cachedImageUrl = null,
        int? width = null,
        int? height = null,
        ChatEntry? chatEntry = null,
        bool isVideo = false)
    {
        var model = new VisualMediaViewerModal.Model(url, cachedImageUrl, altText, width, height, chatEntry, isVideo);
        var modalRef = await ModalUI.Show(model).ConfigureAwait(false);
        await modalRef.WhenClosed.ConfigureAwait(false);
    }
}
