namespace Blazored.Modal;

public interface IModalRef
{
    event EventHandler<ModalCloseRequestEventArgs> ModalCloseRequest;

    Task WhenClosed { get; }

    void Close();
}
