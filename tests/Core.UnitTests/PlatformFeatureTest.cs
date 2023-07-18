namespace ActualChat.Core.UnitTests;

#pragma warning disable VSTHRD104

public class PlatformFeatureTest
{
    [Fact]
    public void DefaultValueTaskTest()
    {
        var vt = default(ValueTask<string?>);
        vt.IsCompleted.Should().BeTrue();
        vt.Result.Should().BeNull();
    }
}
