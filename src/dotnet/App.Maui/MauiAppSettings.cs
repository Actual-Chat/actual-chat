namespace ActualChat.App.Maui;

public sealed record MauiAppSettings
{
    public Uri BaseUri { get; }
    public string BaseUrl { get; }

    public MauiAppSettings(string baseUrl)
    {
        baseUrl = baseUrl.EnsureSuffix("/");
        BaseUrl = baseUrl;
        BaseUri = baseUrl.ToUri();
    }
}
