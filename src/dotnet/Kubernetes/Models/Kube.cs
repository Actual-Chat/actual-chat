using System.Net.Http.Headers;

namespace ActualChat.Kubernetes;

public sealed record Kube : IRequirementTarget
{
    public static Requirement<Kube> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotSupported("This code can execute only within Kubernetes cluster.")),
        (Kube? p) => p != null);

    public static string HttpClientName { get; set; } = "Kube";

    public string Host { get; }
    public int Port { get; }
    public Uri Uri { get; }
    public string PodIP { get; }
    public KubeToken Token { get; }

    public Kube(string host, int port, string podIP, KubeToken token)
    {
        Host = host;
        Port = port;
        Uri = new Uri($"https://{Host}:{Port}/");
        PodIP = podIP;
        Token = token;
    }

    public HttpClient CreateHttpClient(IHttpClientFactory httpClientFactory, string? name = null)
    {
        var client = httpClientFactory.CreateClient(name ?? HttpClientName);
        client.BaseAddress = Uri;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token.Value);
        return client;
    }
}
