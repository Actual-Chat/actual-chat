namespace ActualChat.Kubernetes.IntegrationTests;

public class KubeServicesTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void KubeServiceEndpointsStructuralComparisionDoesntWork()
    {
        var a = new KubeServiceEndpoints(new KubeService("n", "n"),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToApiArray(), true) }.ToApiArray(),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToApiArray(), true) }.ToApiArray(),
            new[] { new KubePort("http", KubeServiceProtocol.Tcp, 8080) }.ToApiArray());

        var b = new KubeServiceEndpoints(new KubeService("n", "n"),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToApiArray(), true) }.ToApiArray(),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToApiArray(), true) }.ToApiArray(),
            new[] { new KubePort("http", KubeServiceProtocol.Tcp, 8080) }.ToApiArray());

        Assert.False(a == b);
    }

    [Fact]
    public void KubeServiceEndpointsToStringIsReadable()
    {
        var a = new KubeServiceEndpoints(new KubeService("n", "n"),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToApiArray(), true) }.ToApiArray(),
            new[] { new KubeEndpoint(new[] { "123.123.12.3" }.ToApiArray(), true) }.ToApiArray(),
            new[] { new KubePort("http", KubeServiceProtocol.Tcp, 8080) }.ToApiArray());

        Out.WriteLine(a.ToString());
    }
}
