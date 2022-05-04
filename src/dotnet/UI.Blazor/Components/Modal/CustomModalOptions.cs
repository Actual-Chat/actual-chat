using Blazored.Modal;

namespace ActualChat.UI.Blazor.Components;

public static class CustomModalOptions
{
    public static ModalOptions Dialog =>
        new ModalOptions {
            HideCloseButton = true,
            Class = "blazored-modal blazored-modal-p0 custom-modal-class",
        };

    public static ModalOptions DialogHeaderless =>
        new ModalOptions {
            HideHeader = true,
            Class = "blazored-modal blazored-modal-p0 custom-modal-class",
        };
}
