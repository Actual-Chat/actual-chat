using System.Net.Http.Headers;

namespace ActualChat.Kubernetes;

public sealed record Kube : IRequirementTarget
{
    public static readonly Requirement<Kube> MustExist = Requirement.New(
        new(() => StandardError.NotSupported("This code can execute only within Kubernetes cluster.")),
        (Kube? k) => k != null);

    public static string HttpClientName { get; set; } = "Kube";

    public string Host { get; }
    public int Port { get; }
    public string Url { get; }
    public Uri Uri { get; }
    public string PodIP { get; }
    public KubeToken Token { get; }
    public bool IsEmulated { get; }

    public Kube(string host, int port, string podIP, KubeToken token)
    {
        Host = host;
        Port = port;
        Url = $"https://{Host}:{Port}/";
        Uri = Url.ToUri();
        PodIP = podIP;
        Token = token;
        IsEmulated = token.IsEmulated;
    }

    public HttpClient CreateHttpClient(IHttpClientFactory httpClientFactory, string? name = null)
    {
        if (IsEmulated)
            throw StandardError.NotSupported("This method can't be used in Kubernetes emulation mode.");

        var client = httpClientFactory.CreateClient(name ?? HttpClientName);
        client.BaseAddress = Uri;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token.Value);
        return client;
    }
}
