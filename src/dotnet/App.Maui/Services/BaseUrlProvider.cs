namespace ActualChat.App.Maui.Services;

public class BaseUrlProvider
{
    public string BaseUrl { get; }

    public BaseUrlProvider(string baseUrl)
    {
        if (!UrlMapper.IsAbsolute(baseUrl))
            throw StandardError.Internal("BaseUrl must be absolute.");

        // Normalize baseUri
        baseUrl = baseUrl.EnsureSuffix("/");
        BaseUrl = baseUrl;
    }
}
