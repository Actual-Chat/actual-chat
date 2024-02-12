namespace ActualChat.Testing.Host;

public class AppHostTestBase<TAppHostFixture> : TestBase
    where TAppHostFixture : AppHostFixture
{
    public TAppHostFixture Fixture { get; }
    public TestAppHost Host { get; }

    public AppHostTestBase(TAppHostFixture fixture, ITestOutputHelper @out) : base(@out)
    {
        Fixture = fixture;
        Host = fixture.Host;
        Host.Output = @out;
    }
}
