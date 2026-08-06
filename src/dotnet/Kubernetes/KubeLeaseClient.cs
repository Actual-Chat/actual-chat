using System.Net;
using System.Net.Sockets;

namespace ActualChat.Kubernetes;

public sealed class KubeLeaseClient(IServiceProvider services)
{
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions WebJsonSerializeOptions = new(JsonSerializerDefaults.Web);

    private IServiceProvider Services { get; } = services;
    private ILogger Log { get; } = services.LogFor<KubeLeaseClient>();
    private IKubeInfo KubeInfo { get; } = services.GetRequiredService<IKubeInfo>();
    private IHttpClientFactory HttpClientFactory { get; } = services.GetRequiredService<IHttpClientFactory>();

    public async Task<Api.Lease?> Get(
        string @namespace, string name,
        TimeSpan? requestTimeout = null,
        CancellationToken cancellationToken = default)
    {
        Log.LogDebug("Getting lease {LeaseName} in namespace {Namespace}", name, @namespace);
        using var cts = CreateTimeoutCts(requestTimeout, cancellationToken);
        var ct = cts?.Token ?? cancellationToken;
        using var httpClient = await CreateHttpClient(ct).ConfigureAwait(false);
        var response = await httpClient.GetAsync(GetUrl(@namespace, name), ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) {
            Log.LogDebug("Lease {LeaseName} not found in namespace {Namespace}", name, @namespace);
            return null;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw StandardError.Constraint(
                "Kubernetes Role/ClusterRole to manage Leases is required for the service account.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Api.Lease>(WebJsonSerializeOptions, ct).ConfigureAwait(false);
    }

    public async Task<Api.Lease> Create(
        string @namespace, Api.Lease lease,
        TimeSpan? requestTimeout = null,
        CancellationToken cancellationToken = default)
    {
        Log.LogDebug("Creating lease {LeaseName} in namespace {Namespace}", lease.Metadata.Name, @namespace);
        var labels = lease.Metadata.Labels;
        if (labels == null || labels.Count == 0)
            throw StandardError.Internal("Lease metadata labels cannot be null or empty.");

        using var cts = CreateTimeoutCts(requestTimeout, cancellationToken);
        var ct = cts?.Token ?? cancellationToken;
        using var httpClient = await CreateHttpClient(ct).ConfigureAwait(false);
        var response = await httpClient.PostAsJsonAsync(GetUrl(@namespace), lease, WebJsonSerializeOptions, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw StandardError.Constraint(
                "Kubernetes Role/ClusterRole to manage Leases is required for the service account.");
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            Log.LogDebug("Lease request is invalid: {StatusCode}, {Content}", response.StatusCode, await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Api.Lease>(WebJsonSerializeOptions, ct).ConfigureAwait(false))!;
    }

    public Api.Lease? GetBlocking(
        string @namespace, string name,
        TimeSpan? requestTimeout = null,
        CancellationToken cancellationToken = default)
    {
        Log.LogDebug("GetBlocking lease {LeaseName} in namespace {Namespace}", name, @namespace);
        using var cts = CreateTimeoutCts(requestTimeout, cancellationToken);
        var ct = cts?.Token ?? cancellationToken;
        using var httpClient = CreateHttpClient(ct).GetAwaiter().GetResult();
        using var request = new HttpRequestMessage(HttpMethod.Get, GetUrl(@namespace, name));
        using var response = httpClient.Send(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw StandardError.Constraint(
                "Kubernetes Role/ClusterRole to manage Leases is required for the service account.");
        response.EnsureSuccessStatusCode();
        using var stream = response.Content.ReadAsStream(ct);
        return JsonSerializer.Deserialize<Api.Lease>(stream, WebJsonSerializeOptions);
    }

    public Api.Lease ReplaceBlocking(
        string @namespace, Api.Lease lease,
        TimeSpan? requestTimeout = null,
        CancellationToken cancellationToken = default)
    {
        Log.LogDebug("ReplaceBlocking lease {LeaseName} in namespace {Namespace}", lease.Metadata.Name, @namespace);
        var labels = lease.Metadata.Labels;
        if (labels == null || labels.Count == 0)
            throw StandardError.Internal("Lease metadata labels cannot be null or empty.");

        using var cts = CreateTimeoutCts(requestTimeout, cancellationToken);
        var ct = cts?.Token ?? cancellationToken;
        using var httpClient = CreateHttpClient(ct).GetAwaiter().GetResult();
        var json = JsonSerializer.Serialize(lease, WebJsonSerializeOptions);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Put, GetUrl(@namespace, lease.Metadata.Name)) { Content = content };
        using var response = httpClient.Send(request, ct);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw StandardError.Constraint(
                "Kubernetes Role/ClusterRole to manage Leases is required for the service account.");
        response.EnsureSuccessStatusCode();
        using var stream = response.Content.ReadAsStream(ct);
        return JsonSerializer.Deserialize<Api.Lease>(stream, WebJsonSerializeOptions)!;
    }

    public async Task<Api.Lease> Replace(
        string @namespace, Api.Lease lease,
        TimeSpan? requestTimeout = null,
        CancellationToken cancellationToken = default)
    {
        Log.LogDebug("Replacing lease {LeaseName} in namespace {Namespace}", lease.Metadata.Name, @namespace);
        var labels = lease.Metadata.Labels;
        if (labels == null || labels.Count == 0)
            throw StandardError.Internal("Lease metadata labels cannot be null or empty.");

        using var cts = CreateTimeoutCts(requestTimeout, cancellationToken);
        var ct = cts?.Token ?? cancellationToken;
        using var httpClient = await CreateHttpClient(ct).ConfigureAwait(false);
        var response = await httpClient.PutAsJsonAsync(GetUrl(@namespace, lease.Metadata.Name), lease, WebJsonSerializeOptions, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw StandardError.Constraint(
                "Kubernetes Role/ClusterRole to manage Leases is required for the service account.");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Api.Lease>(WebJsonSerializeOptions, ct).ConfigureAwait(false))!;
    }

    public async Task<bool> Delete(
        string @namespace, string name,
        TimeSpan? requestTimeout = null,
        CancellationToken cancellationToken = default)
    {
        Log.LogDebug("Deleting lease {LeaseName} in namespace {Namespace}", name, @namespace);
        using var cts = CreateTimeoutCts(requestTimeout, cancellationToken);
        var ct = cts?.Token ?? cancellationToken;
        using var httpClient = await CreateHttpClient(ct).ConfigureAwait(false);
        var response = await httpClient.DeleteAsync(GetUrl(@namespace, name), ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;

        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw StandardError.Constraint(
                "Kubernetes Role/ClusterRole to manage Leases is required for the service account.");
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<Api.LeaseList> List(string @namespace, string? labelSelector = null, CancellationToken cancellationToken = default)
    {
        Log.LogDebug("Listing leases in namespace {Namespace} with label selector '{LabelSelector}'", @namespace, labelSelector ?? "none");

        using var httpClient = await CreateHttpClient(cancellationToken).ConfigureAwait(false);
        var url = GetUrl(@namespace);
        if (!labelSelector.IsNullOrEmpty())
            url += $"?labelSelector={Uri.EscapeDataString(labelSelector)}";
        var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw StandardError.Constraint(
                "Kubernetes Role/ClusterRole to manage Leases is required for the service account.");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Api.LeaseList>(WebJsonSerializeOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task Watch(
        string @namespace,
        string? labelSelector,
        Func<Api.Change<Api.Lease>, CancellationToken, Task> onChange,
        RetryDelaySeq? retryDelays = null,
        CancellationToken cancellationToken = default)
    {
        var resourceVersion = "";
        var failureCount = 0;
        retryDelays ??= RetryDelaySeq.Exp(0.1, 5);

        while (!cancellationToken.IsCancellationRequested)
            try {
                if (resourceVersion.IsNullOrEmpty()) {
                    var list = await List(@namespace, labelSelector, cancellationToken).ConfigureAwait(false);
                    resourceVersion = list.Metadata.ResourceVersion;
                    foreach (var lease in list.Items)
                        await onChange(new Api.Change<Api.Lease>(Api.ChangeType.Added, lease), cancellationToken)
                            .ConfigureAwait(false);
                }

                using var httpClient = await CreateHttpClient(cancellationToken).ConfigureAwait(false);
                var url = GetUrl(@namespace)
                    + $"?watch=true&resourceVersion={resourceVersion}&allowWatchBookmarks=true&timeoutSeconds=30";
                if (!labelSelector.IsNullOrEmpty())
                    url += $"&labelSelector={Uri.EscapeDataString(labelSelector)}";

                Log.LogDebug(
                    "Starting lease watch in namespace {Namespace} with resource version {ResourceVersion} and label selector '{LabelSelector}'",
                    @namespace,
                    resourceVersion,
                    labelSelector ?? "none");
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Gone) {
                    resourceVersion = ""; // Reset RV to perform a fresh List
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.Forbidden)
                    throw StandardError.Constraint(
                        "Kubernetes Role/ClusterRole to manage Leases is required for the service account.");

                response.EnsureSuccessStatusCode();

                failureCount = 0;
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                while (true) {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line == null) // End of stream
                        break;
                    if (line.IsNullOrEmpty())
                        continue;

                    var watchEvent = JsonSerializer.Deserialize<Api.Change<Api.Lease>>(line, WebJsonSerializeOptions);
                    if (watchEvent == null)
                        continue;

                    if (watchEvent.Object is { } lease) {
                        if (!lease.Metadata.ResourceVersion.IsNullOrEmpty())
                            resourceVersion = lease.Metadata.ResourceVersion;

                        if (watchEvent.Type is not (Api.ChangeType.Bookmark or Api.ChangeType.Error))
                            await onChange(new Api.Change<Api.Lease>(watchEvent.Type, lease), cancellationToken)
                                .ConfigureAwait(false);
                    }
                    else if (watchEvent.Type == Api.ChangeType.Error) {
                        // In case of error, we might want to reset RV and restart
                        resourceVersion = "";
                        break;
                    }
                }
            }
            catch (IOException iex) when (iex.InnerException is SocketException {SocketErrorCode: SocketError.ConnectionAborted}) {
                // Handling client cancellation
                if (cancellationToken.IsCancellationRequested)
                    return;

                throw;
            }
            catch (SocketException sex) when (sex.SocketErrorCode == SocketError.ConnectionAborted && cancellationToken.IsCancellationRequested) {
                // Handling client cancellation
                return;
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                if (e is HttpRequestException { StatusCode: HttpStatusCode.Gone }) {
                    resourceVersion = "";
                    continue;
                }

                Log.LogError(e, "Watch failed for leases in namespace {Namespace}", @namespace);
                await Task.Delay(retryDelays[++failureCount], cancellationToken).ConfigureAwait(false);
            }
    }

    // Private methods

    private async Task<HttpClient> CreateHttpClient(CancellationToken cancellationToken)
    {
        var kube = await KubeInfo.RequireKube(cancellationToken).ConfigureAwait(false);
        return kube.CreateHttpClient(HttpClientFactory);
    }

    private static CancellationTokenSource? CreateTimeoutCts(TimeSpan? requestTimeout, CancellationToken cancellationToken)
    {
        if (requestTimeout is not { } timeout)
            return null;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        return cts;
    }

    private static string GetUrl(string @namespace, string? name = null)
    {
        var url = $"apis/coordination.k8s.io/v1/namespaces/{@namespace}/leases";
        if (name != null)
            url += $"/{name}";
        return url;
    }
}
