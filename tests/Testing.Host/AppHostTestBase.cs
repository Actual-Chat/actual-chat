using ActualChat.App.Server;
using ActualChat.Testing.Collections;

namespace ActualChat.Testing.Host;

[Collection(nameof(AppHostTests)), Trait("Category", nameof(AppHostTests))]
public class AppHostTestBase(ITestOutputHelper @out) : TestBase(@out)
{
    protected Task<AppHost> NewAppHost(TestAppHostOptions? options = default)
        => TestAppHostFactory.NewAppHost(Out, options);
}
