using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace ActualChat;

public sealed class EgressHttpHandler : DelegatingHandler
{
    public sealed record Options(
        Func<Uri, bool> IsUriAllowed,
        Func<string, IPAddress, bool> IsAddressAllowed)
    {
        public int MaxRedirectCount { get; init; } = 10;
        public long MaxResponseContentLength { get; init; } = 10 * 1024 * 1024;
    }

    private Options Settings { get; }

    public EgressHttpHandler(Options settings)
        : this(settings, CreateSocketsHttpHandler(settings))
    { }

    public EgressHttpHandler(Options settings, HttpMessageHandler innerHandler)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(settings.MaxRedirectCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.MaxResponseContentLength);
        Settings = settings;
        InnerHandler = innerHandler;
    }

    public static bool IsHttpUri(Uri uri)
        => uri.IsAbsoluteUri
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !uri.DnsSafeHost.IsNullOrEmpty();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var currentRequest = request;
        for (var redirectCount = 0;; redirectCount++) {
            var requestUri = currentRequest.RequestUri;
            if (requestUri is null || !IsHttpUri(requestUri) || !Settings.IsUriAllowed(requestUri))
                throw new HttpRequestException($"Request URI '{requestUri}' is not allowed.");

            var response = await base.SendAsync(currentRequest, cancellationToken).ConfigureAwait(false);
            response.RequestMessage ??= currentRequest;
            if (!TryGetRedirectUri(response, requestUri, out var redirectUri)) {
                LimitResponseContent(response);
                return response;
            }

            var statusCode = response.StatusCode;
            response.Dispose();
            if (redirectCount >= Settings.MaxRedirectCount)
                throw new HttpRequestException($"The redirect limit of {Settings.MaxRedirectCount} was exceeded.");

            currentRequest = CreateRedirectRequest(currentRequest, statusCode, redirectUri);
        }
    }

    // Private methods

    private static SocketsHttpHandler CreateSocketsHttpHandler(Options settings)
        => new () {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = (context, cancellationToken)
                => Connect(settings, context.DnsEndPoint, cancellationToken),
        };

    private static async ValueTask<Stream> Connect(
        Options settings,
        DnsEndPoint endPoint,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(x => !settings.IsAddressAllowed(endPoint.Host, x)))
            throw new HttpRequestException($"Host '{endPoint.Host}' did not resolve to an allowed address.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) {
            NoDelay = true,
        };
        try {
            await socket.ConnectAsync(addresses, endPoint.Port, cancellationToken).ConfigureAwait(false);
            if (socket.RemoteEndPoint is not IPEndPoint remoteEndPoint
                || !settings.IsAddressAllowed(endPoint.Host, remoteEndPoint.Address))
                throw new HttpRequestException($"Host '{endPoint.Host}' connected to an address that is not allowed.");

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch {
            socket.Dispose();
            throw;
        }
    }

    private static bool TryGetRedirectUri(
        HttpResponseMessage response,
        Uri requestUri,
        [NotNullWhen(true)] out Uri? redirectUri)
    {
        redirectUri = null;
        if (response.StatusCode is not (
            HttpStatusCode.MultipleChoices
            or HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect))
            return false;

        var location = response.Headers.Location;
        if (location is null)
            return false;

        redirectUri = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
        return true;
    }

    private static HttpRequestMessage CreateRedirectRequest(
        HttpRequestMessage request,
        HttpStatusCode statusCode,
        Uri redirectUri)
    {
        var method = statusCode == HttpStatusCode.RedirectMethod && request.Method != HttpMethod.Head
            || statusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
                && request.Method == HttpMethod.Post
                ? HttpMethod.Get
                : request.Method;
        var redirectRequest = new HttpRequestMessage(method, redirectUri) {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
            Content = method == request.Method ? request.Content : null,
        };
        foreach (var header in request.Headers)
            redirectRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        redirectRequest.Headers.Authorization = null;
        redirectRequest.Headers.Host = null;
        return redirectRequest;
    }

    private void LimitResponseContent(HttpResponseMessage response)
    {
        var content = response.Content;
        if (content.Headers.ContentLength > Settings.MaxResponseContentLength) {
            response.Dispose();
            throw new HttpRequestException(
                $"Response content length exceeds the {Settings.MaxResponseContentLength}-byte limit.");
        }
        response.Content = new SizeLimitedHttpContent(content, Settings.MaxResponseContentLength);
    }

    private sealed class SizeLimitedHttpContent : HttpContent
    {
        private HttpContent Content { get; }
        private long MaxLength { get; }

        public SizeLimitedHttpContent(HttpContent content, long maxLength)
        {
            Content = content;
            MaxLength = maxLength;
            foreach (var header in content.Headers)
                Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            var source = await Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            try {
                var totalLength = 0L;
                while (true) {
                    var readLength = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (readLength == 0)
                        return;

                    totalLength += readLength;
                    if (totalLength > MaxLength)
                        throw new HttpRequestException($"Response content length exceeds the {MaxLength}-byte limit.");

                    await stream.WriteAsync(buffer.AsMemory(0, readLength), cancellationToken).ConfigureAwait(false);
                }
            }
            finally {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = Content.Headers.ContentLength ?? 0;
            return Content.Headers.ContentLength.HasValue;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Content.Dispose();
            base.Dispose(disposing);
        }
    }
}
