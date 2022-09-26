namespace Blazored.Modal;

public interface IModalReference
{
    Task WhenClosed { get; }

    void Close();
}
