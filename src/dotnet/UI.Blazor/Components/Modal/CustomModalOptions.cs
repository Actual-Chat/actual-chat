using Blazored.Modal;

namespace ActualChat.UI.Blazor.Components;

public static class CustomModalOptions
{
    public static ModalOptions Dialog =>
        new ModalOptions {
            HideCloseButton = true,
            Class = "custom-modal-class",
        };

    public static ModalOptions DialogHeaderless =>
        new ModalOptions {
            HideHeader = true,
            Class = "custom-modal-class",
        };
}
