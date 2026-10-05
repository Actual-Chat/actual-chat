using ActualChat.Testing;

namespace ActualChat.Chat.UnitTests;

public sealed class ApiServiceArchitectureTest
{
    [Theory]
    [InlineData(typeof(Authors))]
    [InlineData(typeof(Roles))]
    [InlineData(typeof(Mentions))]
    public void ApiServicesShouldNotDependOnStorage(Type serviceType)
    {
        // act
        var assertDependencies = () => serviceType.AssertNoStorageDependencies();

        // assert
        assertDependencies.Should().NotThrow();
    }
}
