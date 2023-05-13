using System.Net;
using Java.Nio;
using Java.Nio.Channels;
using Java.Util.Concurrent;
using Microsoft.IO;
using Xamarin.Chromium.CroNet;

namespace ActualChat.App.Maui;

public class CronetMessageHandler : HttpMessageHandler
{
    private static readonly CronetEngine.Builder CronetEngineBuilder = new CronetEngine.Builder(MauiApplication.Current)
        .EnableBrotli(true)
        .EnableHttp2(true)
        .EnableQuic(true)
        .AddQuicHint("dev.actual.chat", 443, 443)
        .AddQuicHint("media-dev.actual.chat", 443, 443)
        .AddQuicHint("cdn-dev.actual.chat", 443, 443)
        .AddQuicHint("actual.chat", 443, 443)
        .AddQuicHint("media.actual.chat", 443, 443)
        .AddQuicHint("cdn.actual.chat", 443, 443);

    private readonly CronetEngine _cronetEngine;

    private IExecutorService Executor { get; }

    public CronetMessageHandler(IExecutorService executor)
    {
        Executor = executor;
        _cronetEngine = CronetEngineBuilder.Build();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Debug.Assert(request.RequestUri != null);

        var callback = new CronetCallback(request, cancellationToken);
        var requestBuilder = _cronetEngine.NewUrlRequestBuilder(
                request.RequestUri.ToString(),
                callback,
                Executor)
            .SetHttpMethod(request.Method.Method)
            .SetPriority(UrlRequest.Builder.RequestPriorityMedium)
            .DisableCache();

        foreach (var header in request.Headers)
            requestBuilder.AddHeader(header.Key, string.Join(',', header.Value));

        if (request.Content != null) {
            var body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            requestBuilder.SetUploadDataProvider(UploadDataProviders.Create(body), Executor);
            foreach (var header in request.Content.Headers)
                requestBuilder.AddHeader(header.Key, string.Join(',', header.Value));
        }
        requestBuilder.Build().Start();

        return await callback.ResultTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _cronetEngine.Dispose();
    }

    // Nested types

    private class CronetCallback : UrlRequest.Callback
    {
        private const int MaxRedirectCount = 3;
        private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new ();

        private readonly TaskCompletionSource<HttpResponseMessage> _resultSource;

        private int _redirectCount;
        private MemoryStream? _responseBody;
        private IWritableByteChannel? _responseBodyChannel;

        private HttpRequestMessage RequestMessage { get; }
        private CancellationToken CancellationToken { get; }

        public Task<HttpResponseMessage> ResultTask => _resultSource.Task;

        public CronetCallback(HttpRequestMessage requestMessage, CancellationToken cancellationToken)
        {
            RequestMessage = requestMessage;
            CancellationToken = cancellationToken;
            _resultSource = TaskCompletionSourceExt.New<HttpResponseMessage>();
        }

        public override void OnFailed(UrlRequest p0, UrlResponseInfo p1, CronetException p2)
        {
            CleanUp();
            _resultSource.TrySetException(p2);
        }

        public override void OnRedirectReceived(UrlRequest p0, UrlResponseInfo p1, string p2)
        {
            if (CancellationToken.IsCancellationRequested) {
                p0.Cancel();
                return;
            }
            if (_redirectCount++ > MaxRedirectCount) {
                CleanUp();
                p0.Cancel();
            }
            p0.FollowRedirect();
        }

        public override void OnResponseStarted(UrlRequest p0, UrlResponseInfo p1)
        {
            if (CancellationToken.IsCancellationRequested) {
                p0.Cancel();
                return;
            }
            _responseBody = MemoryStreamManager.GetStream();
            _responseBodyChannel = Java.Nio.Channels.Channels.NewWritableChannel(_responseBody)!;

            p0.Read(ByteBuffer.AllocateDirect(4096));
        }

        public override void OnReadCompleted(UrlRequest p0, UrlResponseInfo p1, ByteBuffer p2)
        {
            if (CancellationToken.IsCancellationRequested) {
                p0.Cancel();
                return;
            }
            p2.Flip();
            try {
                _responseBodyChannel ??= Java.Nio.Channels.Channels.NewWritableChannel(_responseBody)!;
                _responseBodyChannel.Write(p2);
                p2.Clear();
                p0.Read(p2);
            }
            catch (Exception e) {
                p0.Cancel();
                _resultSource.TrySetException(e);
            }
        }

        public override void OnSucceeded(UrlRequest p0, UrlResponseInfo p1)
        {
            _responseBodyChannel?.DisposeSilently();
            _responseBodyChannel = null;

            var ret = new HttpResponseMessage((HttpStatusCode)p1.HttpStatusCode) {
                RequestMessage = RequestMessage,
                ReasonPhrase = p1.HttpStatusText,
                Version = string.IsNullOrWhiteSpace(p1.NegotiatedProtocol)
                    ? HttpVersion.Version11
                    : OrdinalEquals(p1.NegotiatedProtocol, "h2")
                        ? HttpVersion.Version20
                        : HttpVersion.Version30,
            };
            if (_responseBody != null) {
                _responseBody.Flush();
                _responseBody.Position = 0;
                ret.Content = new StreamContent(_responseBody);
                _responseBody = null;
            }
            else
                ret.Content = new StreamContent(Stream.Null);

            foreach (var (key, values) in p1.AllHeaders)
                if (key.StartsWith("content", StringComparison.OrdinalIgnoreCase))
                    ret.Content.Headers.Add(key, values);
                else
                    ret.Headers.Add(key, values);
            // we are waiting for transmit completion - but we can try to return Response as soon as possible with uncompleted StreamContent
            _resultSource.TrySetResult(ret);
        }

        public override void OnCanceled(UrlRequest request, UrlResponseInfo info)
        {
            CleanUp();
            _ = _resultSource.TrySetCanceled(CancellationToken);
            base.OnCanceled(request, info);
        }

        private void CleanUp()
        {
            _responseBodyChannel?.DisposeSilently();
            _responseBody?.DisposeSilently();
            _responseBodyChannel = null;
            _responseBody = null;
        }
    }
}
