namespace ActualChat.Kubernetes.IntegrationTests;

public class KubeServicesTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void KubeServiceEndpointsStructuralComparisionDoesntWork()
    {
        var a = new KubeServiceEndpoints(new KubeService("n", "n"),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToArray(), true) }.ToArray(),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToArray(), true) }.ToArray(),
            new[] { new KubePort("http", KubeServiceProtocol.Tcp, 8080) }.ToArray());

        var b = new KubeServiceEndpoints(new KubeService("n", "n"),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToArray(), true) }.ToArray(),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToArray(), true) }.ToArray(),
            new[] { new KubePort("http", KubeServiceProtocol.Tcp, 8080) }.ToArray());

        Assert.False(a == b);
    }

    [Fact]
    public void KubeServiceEndpointsToStringIsReadable()
    {
        var a = new KubeServiceEndpoints(new KubeService("n", "n"),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToArray(), true) }.ToArray(),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToArray(), true) }.ToArray(),
            new[] { new KubePort("http", KubeServiceProtocol.Tcp, 8080) }.ToArray());

        WriteLine(a.ToString());
    }
}
