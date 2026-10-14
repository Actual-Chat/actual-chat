using ActualChat.Rpc;

namespace ActualChat.Api.Contracts.UnitTests.Rpc;

public sealed class FileRpcPathStoreTest(ITestOutputHelper @out) : TestBase(@out), IDisposable
{
    private readonly string _folder
        = Path.Combine(Path.GetTempPath(), "rpc-path-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    [Fact]
    public async Task SavedDataShouldBeLoadedBack()
    {
        // arrange
        var store = new FileRpcPathStore(_folder);

        // act
        await store.Save("net", [1, 2, 3], default);

        // assert
        (await store.Load("net", default)).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task UnknownNetworkShouldLoadNothing()
    {
        // arrange
        var store = new FileRpcPathStore(_folder);

        // act
        var data = await store.Load("net", default);

        // assert
        data.Should().BeNull();
    }

    [Fact]
    public async Task NetworkIdShouldNotReachTheFileSystem()
    {
        // arrange
        var store = new FileRpcPathStore(_folder);

        // act
        await store.Save("Home WiFi", [1], default);

        // assert
        Directory.EnumerateFiles(_folder).Should().ContainSingle()
            .Which.Should().NotContainEquivalentOf("wifi");
    }

    [Fact]
    public async Task LeastRecentlyUsedNetworksShouldBeDropped()
    {
        // arrange
        var store = new FileRpcPathStore(_folder, 3);
        foreach (var name in new[] { "a", "b", "c" }) {
            await store.Save(name, [1], default);
            await Task.Delay(30);
        }

        // act: "a" is used again, so "b" is the least recent one
        await store.Load("a", default);
        await Task.Delay(30);
        await store.Save("d", [1], default);

        // assert
        (await store.Load("b", default)).Should().BeNull();
        foreach (var name in new[] { "a", "c", "d" })
            (await store.Load(name, default)).Should().NotBeNull(name);
    }

    [Fact]
    public async Task SaveShouldLeaveNoTemporaryFiles()
    {
        // arrange
        var store = new FileRpcPathStore(_folder);

        // act
        await store.Save("net", [1], default);
        await store.Save("net", [2], default);

        // assert
        Directory.EnumerateFiles(_folder).Should().ContainSingle()
            .Which.Should().EndWith(".rpcpath");
    }
}
