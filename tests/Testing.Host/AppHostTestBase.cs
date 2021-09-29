using ActualChat.Testing.Collections;
using Stl.Testing;
using Xunit.Abstractions;

namespace ActualChat.Testing.Host
{
    [Collection(nameof(AppHostTests)), Trait("Category", nameof(AppHostTests))]
    public class AppHostTestBase : TestBase
    {
        public AppHostTestBase(ITestOutputHelper @out) : base(@out) { }
    }
}
