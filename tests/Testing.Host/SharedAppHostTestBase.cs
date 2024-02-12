namespace ActualChat.Testing.Host;

public abstract class SharedAppHostTestBase<TAppHostFixture> : TestBase
    where TAppHostFixture : AppHostFixture
{
    public TAppHostFixture Fixture { get; }
    public TestAppHost AppHost { get; }

    protected SharedAppHostTestBase(TAppHostFixture fixture, ITestOutputHelper @out) : base(@out)
    {
        Fixture = fixture;
        AppHost = fixture.Host;
        AppHost.Output = @out;
    }
}
