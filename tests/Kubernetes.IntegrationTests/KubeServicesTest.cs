namespace ActualChat.Kubernetes.IntegrationTests;

public class KubeServicesTest: TestBase
{
    public KubeServicesTest(ITestOutputHelper @out) : base(@out)
    {
    }

    [Fact]
    public void KubeServiceEndpointsStructuralComparisionDoesntWork()
    {
        var a = new KubeServiceEndpoints(new KubeService("n", "n"),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToImmutableArray(), true) }.ToImmutableArray(),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToImmutableArray(), true) }.ToImmutableArray(),
            new[] { new KubePort("http", KubeServiceProtocol.Tcp, 8080) }.ToImmutableArray());

        var b = new KubeServiceEndpoints(new KubeService("n", "n"),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToImmutableArray(), true) }.ToImmutableArray(),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToImmutableArray(), true) }.ToImmutableArray(),
            new[] { new KubePort("http", KubeServiceProtocol.Tcp, 8080) }.ToImmutableArray());

        Assert.False(a == b);
    }

    [Fact]
    public void KubeServiceEndpointsToStringIsReadable()
    {
        var a = new KubeServiceEndpoints(new KubeService("n", "n"),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToImmutableArray(), true) }.ToImmutableArray(),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToImmutableArray(), true) }.ToImmutableArray(),
            new[] { new KubePort("http", KubeServiceProtocol.Tcp, 8080) }.ToImmutableArray());

        Out.WriteLine(a.ToString());
    }
}
