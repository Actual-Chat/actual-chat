namespace Blazored.Modal.Services;

public class ModalService
{
    internal event Func<ModalReference, Task>? OnModalInstanceAdded;
    internal event Func<ModalReference, Task>? OnModalCloseRequested;

    /// <summary>
    /// Shows the modal.
    /// </summary>
    /// <param name="modalContent">Modal to display.</param>
    /// <param name="options">Options to configure the modal.</param>
    public IModalReference Show(RenderFragment modalContent, ModalOptions options)
    {
        ModalReference? modalReference = null;
        var modalInstanceId = Guid.NewGuid();
        var modalInstance = new RenderFragment(builder => {
            builder.OpenComponent<BlazoredModalInstance>(0);
            builder.SetKey(modalInstanceId);
            builder.AddAttribute(1, "Options", options);
            builder.AddAttribute(2, "Content", modalContent);
            builder.AddAttribute(3, "Id", modalInstanceId);
            builder.AddComponentReferenceCapture(5, compRef => modalReference!.ModalInstanceRef = (BlazoredModalInstance)compRef);
            builder.CloseComponent();
        });
        modalReference = new ModalReference(modalInstanceId, modalInstance, this);

        OnModalInstanceAdded?.Invoke(modalReference);

        return modalReference;
    }

    internal void Close(ModalReference modal)
        => OnModalCloseRequested?.Invoke(modal);
}
