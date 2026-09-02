namespace ActualChat.UI.Blazor.Components;

public sealed record ShareQrModalModel(
    string Title,
    LocalUrl Link,
    string? ImageUrl = null);
