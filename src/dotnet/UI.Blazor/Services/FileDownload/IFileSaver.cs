namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Saves remote content to the device. Implemented only on platforms that need a
/// native save; where it's absent, <see cref="FileDownloadUI"/> falls back to a
/// browser download.
/// </summary>
public interface IFileSaver
{
    // Takes the whole group so the implementation reports it as one outcome
    // rather than one toast (and, on iOS, one share sheet) per file.
    Task Save(IReadOnlyList<FileToSave> files);
}
