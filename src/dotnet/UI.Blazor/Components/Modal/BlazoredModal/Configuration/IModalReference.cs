namespace Blazored.Modal;

public interface IModalReference
{
    event EventHandler<ModalInstanceCloseRequestedEventArgs> ModalInstanceCloseRequested;
    
    Task WhenClosed { get; }

    void Close();
}
