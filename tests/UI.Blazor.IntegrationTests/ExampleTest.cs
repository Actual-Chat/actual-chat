using ActualChat.Testing.Host;

namespace ActualChat.UI.Blazor.IntegrationTests;

public class ExampleTest : AppHostTestBase
{
    public ExampleTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task SessionTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var sessionFactory = services.GetRequiredService<ISessionFactory>();
        var sessionA = sessionFactory.CreateSession();

        Assert.NotNull(sessionA);
        sessionA.ToString().Length.Should().BeGreaterOrEqualTo(16);
    }
}
