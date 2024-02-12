namespace ActualChat.Testing.Host;

public class AppHostTestBase<TAppHostFixture> : TestBase
    where TAppHostFixture : AppHostFixture
{
    public TAppHostFixture Fixture { get; }
    public TestAppHost AppHost { get; }

    public AppHostTestBase(TAppHostFixture fixture, ITestOutputHelper @out) : base(@out)
    {
        Fixture = fixture;
        AppHost = fixture.Host;
        AppHost.Output = @out;
    }
}
