namespace Blazored.Modal.Services;

public class ModalService
{
    internal event Func<ModalRef, Task>? OnModalInstanceAdded;
    internal event Func<ModalRef, Task>? OnModalCloseRequested;

    /// <summary>
    /// Shows the modal.
    /// </summary>
    /// <param name="modalContent">Modal to display.</param>
    /// <param name="options">Options to configure the modal.</param>
    public IModalRef Show(RenderFragment modalContent, ModalOptions options)
    {
        ModalRef? modalReference = null;
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
        modalReference = new ModalRef(modalInstanceId, modalInstance, this);

        OnModalInstanceAdded?.Invoke(modalReference);

        return modalReference;
    }

    internal void Close(ModalRef modal)
        => OnModalCloseRequested?.Invoke(modal);
}
