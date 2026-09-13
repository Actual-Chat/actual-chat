using ActualChat.ContentCaching;

namespace ActualChat.App.Maui;

internal static class MauiContentRequests
{
    private static readonly LoggingContentHandler PassThroughHandler = new(log: StaticLog.For<LoggingContentHandler>());

    public static void Observe(string? url, string? method)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        var request = new ContentRequest(uri) { Method = new HttpMethod(method.NullIfEmpty() ?? "GET") };
        using var response = PassThroughHandler.Handle(request).GetAwaiter().GetResult();
    }
}
