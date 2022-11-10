namespace ActualChat.SignalR;

public static class UrlMapperExt
{
    public static string GetHubUrl(this UrlMapper urlMapper, string relativeUrl)
    {
        var hubUrl = urlMapper.ToAbsolute(relativeUrl, true);

        // Workaround for missing SSL CA cert for local.actual.chat
        if (urlMapper.IsLocalActualChat && hubUrl.OrdinalHasPrefix("https://local.actual.chat/backend/hub/", out var suffix))
            hubUrl = "http://local.actual.chat:7080/backend/hub/" + suffix;

        return hubUrl;
    }
}
