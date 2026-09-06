namespace ActualChat.UI.Blazor.Components;

// ScanHandler returns true when it consumed a scanned link; otherwise the modal navigates to it
public sealed record ShareQrModalModel(
    string Title,
    LocalUrl Link,
    string? ImageUrl = null,
    Func<LocalUrl, Task<bool>>? ScanHandler = null)
{
    // Overrides the encoded URL when set - lets an off-origin link with no LocalUrl form still yield a QR
    public string? AbsoluteUrl { get; init; }
}
