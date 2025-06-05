using ActualChat.Flows;
using ActualChat.Hashing;

namespace ActualChat.Core.Server.UnitTests.Hashing;

public class StableHashTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ResolverTest()
    {
        var resolver = StableHash.Resolver;
        resolver.Get<Unit>().Should().NotBeNull();
        resolver.Get<int>().Should().NotBeNull();
        resolver.Get<int?>().Should().NotBeNull();
        resolver.Get<(int, long?)>().Should().NotBeNull();
        resolver.Get<string>().Should().NotBeNull();
        resolver.Get<Symbol>().Should().NotBeNull();
        resolver.Get<Session>().Should().NotBeNull();
        resolver.Get<UserId>().Should().NotBeNull(); // IStringIdentifier
        resolver.Get<FlowId>().Should().NotBeNull(); // ISymbolIdentifier
        resolver.Get<FlowId?>().Should().NotBeNull(); // ISymbolIdentifier?

        resolver.Get<double>().Should().BeNull();
        resolver.Get<double?>().Should().BeNull();
        resolver.Get<(int, double)>().Should().BeNull();
        resolver.Get<(int, double?)>().Should().BeNull();
    }

    [Fact]
    public void ComputeTest()
    {
        StableHash.Compute(1).Should().Be(1);
        StableHash.Compute((int?)1).Should().Be(1);
        StableHash.Compute("").Should().NotBe(0);

        StableHash.Compute((int?)null).Should().Be(0);
        StableHash.Compute(0d).Should().Be(0);
        StableHash.Compute((string?)null).Should().Be(0);
    }
}
