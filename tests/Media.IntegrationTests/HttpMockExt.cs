using System.Net;
using System.Net.Http.Headers;

namespace ActualChat.Media.IntegrationTests;

public static class HttpMockExt
{
    public static HttpHandlerMock SetupHtml(
        this HttpHandlerMock mock,
        string url,
        Action<OpenGrapHtmlBuilder> htmlBuilder)
    {
        var builder = new OpenGrapHtmlBuilder();
        htmlBuilder(builder);
        return mock.Setup(url,
            req => new (HttpStatusCode.OK) {
                RequestMessage = req,
                Content = builder.BuildHtmlResponseContent(),
            });
    }

    public static HttpHandlerMock SetupImage(
        this HttpHandlerMock mock,
        string url,
        string resourceName = "default.jpg",
        string contentType = "image/jpeg")
        => mock.Setup(url,
            req => new (HttpStatusCode.OK) {
                RequestMessage = req,
                Content = new StreamContent(GetImgStream(resourceName)) {
                    Headers = { ContentType = MediaTypeHeaderValue.Parse(contentType) },
                },
            });

    public static HttpHandlerMock SetupRobotsNotFound(this HttpHandlerMock mock, string url)
        => mock.Setup(GetRobotsUrl(url),
            req => new (HttpStatusCode.NotFound) {
                RequestMessage = req,
            });

    public static HttpHandlerMock SetupEmptyRobots(this HttpHandlerMock mock, string url)
        => mock.Setup(GetRobotsUrl(url),
            req => new (HttpStatusCode.OK) {
                RequestMessage = req,
                Content = new StringContent("", MediaTypeHeaderValue.Parse("text/plain")),
            });

    public static HttpHandlerMock SetupAllAllowedRobots(this HttpHandlerMock mock, string url)
        => mock.Setup(GetRobotsUrl(url),
            req => new (HttpStatusCode.OK) {
                RequestMessage = req,
                Content = new StringContent("""
                                            User-agent: *
                                            """, MediaTypeHeaderValue.Parse("text/plain")),
            });

    public static HttpHandlerMock SetupAllDisallowedRobots(this HttpHandlerMock mock, string url)
        => mock.Setup(GetRobotsUrl(url),
            req => new (HttpStatusCode.OK) {
                RequestMessage = req,
                Content = new StringContent("""
                                            User-agent: *
                                            Disallow: /
                                            """,
                    MediaTypeHeaderValue.Parse("text/plain")),
            });

    private static string GetRobotsUrl(string url)
        => new Uri(new Uri(url), "/robots.txt").AbsoluteUri;

    private static Stream GetImgStream(string name)
    {
        var type = typeof(HttpMockExt);
        return type.Assembly.GetManifestResourceStream($"{type.Namespace}.TestImages.{name}").Require();
    }
}
