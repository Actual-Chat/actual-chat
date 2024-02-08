using ActualChat.Testing.Host.Collections;

namespace ActualChat.Testing.Host;

[Collection(nameof(AppHostTestCollection)), Trait("Category", nameof(AppHostTestCollection))]
public class AppHostTestBase(ITestOutputHelper @out, AppHostFixture? fixture=null) : TestBase(@out)
{
    public TestAppHost? Host => fixture?.Host;

    protected Task<TestAppHost> NewAppHost(TestAppHostOptions? options = default)
        => TestAppHostFactory.NewAppHost(Out, options);
}
