using ActualChat.Testing.Collections;

namespace ActualChat.Testing.Host
{
    [Collection(nameof(AppHostTests)), Trait("Category", nameof(AppHostTests))]
    public class AppHostTestBase : TestBase
    {
        public AppHostTestBase(ITestOutputHelper @out) : base(@out) { }
    }
}
