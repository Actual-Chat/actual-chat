using System.Net;
using System.Net.Http.Headers;

namespace ActualChat.Media.IntegrationTests;

public static class HttpMockExt
{
    public static HttpHandlerMock SetupHtmlResponse(this HttpHandlerMock mock, string url, Action<OpenGrapHtmlBuilder> htmlBuilder)
    {
        var builder = new OpenGrapHtmlBuilder();
        htmlBuilder(builder);
        return mock.Setup(url,
            req => new (HttpStatusCode.OK) {
                RequestMessage = req,
                Content = builder.BuildHtmlResponseContent(),
            });
    }

    public static HttpHandlerMock SetupImageResponse(this HttpHandlerMock mock, string url, string resourceName = "default.jpg", string contentType = "image/jpeg")
        => mock.Setup(url, req => new (HttpStatusCode.OK) {
            RequestMessage = req,
            Content = new StreamContent(GetImgStream(resourceName)) {
                Headers = { ContentType = MediaTypeHeaderValue.Parse(contentType) },
            },
        });

    private static Stream GetImgStream(string name)
    {
        var type = typeof(HttpMockExt);
        return type.Assembly.GetManifestResourceStream($"{type.Namespace}.TestImages.{name}").Require();
    }
}
