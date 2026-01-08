using System.Net;
using System.Net.Http.Json;

namespace ActualChat.Kubernetes;

public sealed class KubeLeaseClient(IServiceProvider services)
{
    private static readonly JsonSerializerOptions WebJsonSerializeOptions = new(JsonSerializerDefaults.Web);

    private IServiceProvider Services { get; } = services;
    private ILogger Log { get; } = services.LogFor<KubeLeaseClient>();

    public async Task<Api.Lease?> Get(string @namespace, string name, CancellationToken cancellationToken = default)
    {
        using var httpClient = await CreateHttpClient(cancellationToken).ConfigureAwait(false);
        var response = await httpClient.GetAsync(GetUrl(@namespace, name), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw StandardError.Constraint(
                "Kubernetes Role/ClusterRole to manage Leases is required for the service account.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Api.Lease>(WebJsonSerializeOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Api.Lease> Create(string @namespace, Api.Lease lease, CancellationToken cancellationToken = default)
    {
        using var httpClient = await CreateHttpClient(cancellationToken).ConfigureAwait(false);
        var response = await httpClient.PostAsJsonAsync(GetUrl(@namespace), lease, WebJsonSerializeOptions, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw StandardError.Constraint(
                "Kubernetes Role/ClusterRole to manage Leases is required for the service account.");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Api.Lease>(WebJsonSerializeOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<Api.Lease> Replace(string @namespace, Api.Lease lease, CancellationToken cancellationToken = default)
    {
        using var httpClient = await CreateHttpClient(cancellationToken).ConfigureAwait(false);
        var response = await httpClient.PutAsJsonAsync(GetUrl(@namespace, lease.Metadata.Name), lease, WebJsonSerializeOptions, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw StandardError.Constraint(
                "Kubernetes Role/ClusterRole to manage Leases is required for the service account.");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Api.Lease>(WebJsonSerializeOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<bool> Delete(string @namespace, string name, CancellationToken cancellationToken = default)
    {
        using var httpClient = await CreateHttpClient(cancellationToken).ConfigureAwait(false);
        var response = await httpClient.DeleteAsync(GetUrl(@namespace, name), cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken = default)
    {
        var resourceVersion = "";
        var failureCount = 0;
        var retryDelays = RetryDelaySeq.Exp(1, 30);

        while (!cancellationToken.IsCancellationRequested)
            try {
                if (resourceVersion.IsNullOrEmpty()) {
                    var list = await List(@namespace, labelSelector, cancellationToken).ConfigureAwait(false);
                    resourceVersion = list.Metadata.ResourceVersion;
                    foreach (var lease in list.Items)
                        await onChange(new Api.Change<Api.Lease>(Api.ChangeType.Added, lease), cancellationToken).ConfigureAwait(false);
                }

                using var httpClient = await CreateHttpClient(cancellationToken).ConfigureAwait(false);
                var url = GetUrl(@namespace) + $"?watch=true&resourceVersion={resourceVersion}&allowWatchBookmarks=true&timeoutSeconds=300";
                if (!labelSelector.IsNullOrEmpty())
                    url += $"&labelSelector={Uri.EscapeDataString(labelSelector)}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
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
                while (!reader.EndOfStream) {
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line.IsNullOrEmpty())
                        continue;

                    var watchEvent = JsonSerializer.Deserialize<Api.Change<Api.Lease>>(line, WebJsonSerializeOptions);
                    if (watchEvent == null)
                        continue;

                    if (watchEvent.Object is {} lease) {
                        if (!lease.Metadata.ResourceVersion.IsNullOrEmpty())
                            resourceVersion = lease.Metadata.ResourceVersion;

                        if (watchEvent.Type is not (Api.ChangeType.Bookmark or Api.ChangeType.Error))
                            await onChange(new Api.Change<Api.Lease>(watchEvent.Type, lease), cancellationToken).ConfigureAwait(false);
                    }
                    else if (watchEvent.Type == Api.ChangeType.Error) {
                        // In case of error, we might want to reset RV and restart
                        resourceVersion = "";
                        break;
                    }
                }
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
        var kubeInfo = Services.GetRequiredService<IKubeInfo>();
        var kube = await kubeInfo.RequireKube(cancellationToken).ConfigureAwait(false);
        return kube.CreateHttpClient(Services.HttpClientFactory());
    }

    private static string GetUrl(string @namespace, string? name = null)
    {
        var url = $"apis/coordination.k8s.io/v1/namespaces/{@namespace}/leases";
        if (name != null)
            url += $"/{name}";
        return url;
    }
}
