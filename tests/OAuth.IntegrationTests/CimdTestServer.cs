using System.Collections.Concurrent;
using System.Net;
using System.Text;
using ActualLab.Testing.Web;

namespace ActualChat.OAuth.IntegrationTests;

/// <summary>
/// Serves client ID metadata documents over plain http on a free local port; one document per name.
/// </summary>
public sealed class CimdTestServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<string, string> _documents = new();
    private readonly CancellationTokenSource _stopCts = new();
    private readonly Task _serveTask;
    private int _fetchCount;

    public Uri BaseUri { get; }
    public int FetchCount => Volatile.Read(ref _fetchCount);

    public CimdTestServer()
    {
        BaseUri = WebTestHelpers.GetUnusedLocalUri();
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

    public string Publish(string name, Func<string, object> document)
    {
        // The builder gets the document's own URL, which a valid document must embed as client_id
        var path = $"/cimd/{name}.json";
        var url = new Uri(BaseUri, path).ToString();
        _documents[path] = JsonSerializer.Serialize(document(url));
        return url;
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

            Interlocked.Increment(ref _fetchCount);
            if (_documents.TryGetValue(context.Request.Url!.AbsolutePath, out var json)) {
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(json));
            }
            else
                context.Response.StatusCode = 404;
            context.Response.Close();
        }
    }
}
