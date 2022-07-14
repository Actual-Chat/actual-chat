namespace ActualChat.App.Maui;

public record ClientAppSettings
{
    public string BaseUri { get; set; } = "";

    public string SessionId { get; init; } = null!;
}
