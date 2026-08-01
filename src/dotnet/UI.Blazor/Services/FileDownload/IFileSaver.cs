namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Saves remote content to the device. Implemented only on platforms that need a
/// native save; where it's absent, <see cref="FileDownloadUI"/> falls back to a
/// browser download.
/// </summary>
public interface IFileSaver
{
    Task Save(string url, string fileName, string contentType);
}
