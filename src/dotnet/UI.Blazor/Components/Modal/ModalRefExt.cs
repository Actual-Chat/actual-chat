namespace ActualChat.UI.Blazor.Components;

public static class ModalRefExt
{
    extension(ModalRef modalRef)
    {
        public bool IsFullScreen(bool isNarrow)
            => isNarrow || modalRef.Options.OverlayClass.Contains(ModalOptions.FullScreenOverlayClass);
    }
}
