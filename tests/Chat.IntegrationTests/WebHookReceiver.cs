using System.Net;
using System.Text;
using ActualLab.Testing.Web;

namespace ActualChat.Chat.IntegrationTests;

/// <summary>
/// Records every request a web hook POSTs to it on a free local port
/// and answers with the status its request index maps to (200 by default).
/// </summary>
public sealed class WebHookReceiver : IAsyncDisposable
{
    public sealed record Received(int Index, IReadOnlyDictionary<string, string> Headers, string Body);

    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly HttpListener _listener = new();
    private readonly Channel<Received> _received = Channel.CreateUnbounded<Received>();
    private readonly CancellationTokenSource _stopCts = new();
    private readonly Task _serveTask;
    private int _requestCount;

    public Uri BaseUri { get; }
    public string HookUrl => new Uri(BaseUri, "/hook").ToString();
    public Func<int, HttpStatusCode> StatusFor { get; set; } = _ => HttpStatusCode.OK;
    public bool ServeImage { get; set; }

    public WebHookReceiver()
    {
        // The egress guard rejects a literal IP host, so the hook has to address the receiver by name
        BaseUri = new Uri($"http://localhost:{WebTestHelpers.GetUnusedTcpPort()}/");
        _listener.Prefixes.Add(BaseUri.ToString());
        _listener.Start();
        _serveTask = Task.Run(Serve);
    }

    public async ValueTask DisposeAsync()
    {
        await _stopCts.CancelAsync();
        _listener.Stop();
        await _serveTask.SilentAwait();
    }

    public async Task<Received> Next(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await _received.Reader.ReadAsync(cts.Token);
    }

    // Private methods

    private async Task Serve()
    {
        while (!_stopCts.IsCancellationRequested) {
            HttpListenerContext context;
            try {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (_stopCts.IsCancellationRequested) {
                return;
            }

            if (ServeImage && context.Request.HttpMethod == "GET") {
                var imageResponse = context.Response;
                imageResponse.ContentType = "image/png";
                imageResponse.ContentLength64 = OnePixelPng.Length;
                await imageResponse.OutputStream.WriteAsync(OnePixelPng);
                imageResponse.Close();
                continue;
            }

            var index = Interlocked.Increment(ref _requestCount) - 1;
            var request = context.Request;
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var headers = request.Headers.AllKeys
                .OfType<string>()
                .ToDictionary(key => key, key => request.Headers[key]!, StringComparer.OrdinalIgnoreCase);
            context.Response.StatusCode = (int)StatusFor(index);
            context.Response.Close();
            _received.Writer.TryWrite(new Received(index, headers, body));
        }
    }
}
