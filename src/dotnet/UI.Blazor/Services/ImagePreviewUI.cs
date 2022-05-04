using Blazored.Modal;
using Blazored.Modal.Services;

namespace ActualChat.UI.Blazor.Services;

public class ImagePreviewUI
{
    private readonly IModalService _modalService;

    public ImagePreviewUI(IModalService modalService)
        => _modalService = modalService;

    public Task Show(string url, string? altText = null)
    {
        var modalParameters = new ModalParameters();
        modalParameters.Add(nameof(ImagePreview.Url), url);
        modalParameters.Add(nameof(ImagePreview.AltText), altText);

        var modalOptions = new ModalOptions {
            Animation = ModalAnimation.FadeIn(0.2),
            HideHeader = true,
            Class = "blazored-modal blazored-modal-p0 blazored-modal-transparent blazored-modal-border-none",
        };
        var reference = _modalService.Show<ImagePreview>(null, modalParameters, modalOptions);
        return reference.Result;
    }
}
