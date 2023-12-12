namespace ActualChat.UI.Blazor.Components;

public interface IMediaSaver
{
    Task Save(string uri, string contentType);
}
