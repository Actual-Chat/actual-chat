using System.Security.Cryptography;
using ActualChat.Kvas;
using ActualLab.IO;

namespace ActualChat.Core.UnitTests.Kvas;

public class KvasarKvasTest(ITestOutputHelper @out) : TestBase(@out), IAsyncLifetime
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    private FilePath _basePath;

    public Task InitializeAsync()
    {
        var directory = FilePath.GetApplicationTempDirectory() & $"KvasarKvasTest-{Ulid.NewUlid()}";
        Directory.CreateDirectory(directory);
        _basePath = directory & "Store";
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try {
            Directory.Delete(_basePath.DirectoryPath, true);
        }
        catch (IOException) {
            // Intended: a leftover temp directory doesn't fail the test
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task BasicTest()
    {
        // arrange
        var services = CreateServices();
        await using var kvas = CreateKvas(services);

        // act
        await kvas.Set("a", "a");
        await kvas.Set("b", Box.New(1));
        await kvas.Set("c", "");
        await kvas.Set("d", "d");
        await kvas.Set("d", (string?)null);

        // assert
        (await kvas.Get<string>("a")).Should().Be("a");
        (await kvas.Get<Box<int>>("b"))!.Value.Should().Be(1);
        (await kvas.Get<string>("c")).Should().Be("");
        (await kvas.Get<string>("d")).Should().BeNull();
        (await kvas.Get<string>("missing")).Should().BeNull();
    }

    [Fact]
    public async Task SetManyTest()
    {
        // arrange
        var services = CreateServices();
        await using var kvas = CreateKvas(services);
        await kvas.Set("gone", [1, 2, 3]);

        // act
        await kvas.SetMany([("x", [1]), ("y", [2, 2]), ("gone", null)]);

        // assert
        (await kvas.Get("x")).Should().Equal([1]);
        (await kvas.Get("y")).Should().Equal([2, 2]);
        (await kvas.Get("gone")).Should().BeNull();
    }

    [Fact]
    public async Task ReopenPreservesEntriesTest()
    {
        // arrange
        var services = CreateServices();
        await using (var kvas = CreateKvas(services)) {
            await kvas.Set("a", "a");
            await kvas.Set("b", "b");
            await kvas.Flush();
        }

        // act
        await using var kvas2 = CreateKvas(services);

        // assert
        (await kvas2.Get<string>("a")).Should().Be("a");
        var entries = await kvas2.ListAllEntries();
        entries.Select(x => x.Key).Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public async Task VersionChangeWipesStoreTest()
    {
        // arrange
        var services = CreateServices();
        await using (var kvas = CreateKvas(services, version: "1.0")) {
            await kvas.Set("a", "a");
            await kvas.Flush();
        }

        // act
        await using var kvas2 = CreateKvas(services, version: "2.0");

        // assert
        (await kvas2.Get<string>("a")).Should().BeNull();
    }

    [Fact]
    public async Task ClearTest()
    {
        // arrange
        var services = CreateServices();
        await using var kvas = CreateKvas(services);
        await kvas.Set("a", "a");

        // act
        await kvas.Clear();

        // assert
        (await kvas.Get<string>("a")).Should().BeNull();
        (await kvas.ListAllEntries()).Should().BeEmpty();
    }

    [Fact]
    public async Task SuspendAndResumeTest()
    {
        // arrange
        var services = CreateServices();
        await using var kvas = CreateKvas(services);
        await kvas.Set("a", "a");

        // act
        await kvas.Suspend();
        var whenSuspended = await kvas.Get<string>("a");
        kvas.Resume();

        // assert
        whenSuspended.Should().BeNull();
        (await kvas.Get<string>("a")).Should().Be("a");
    }

    [Fact]
    public async Task ContendedOpenDegradesToNoOpTest()
    {
        // arrange
        var services = CreateServices();
        await using var kvas = CreateKvas(services);
        await kvas.Set("a", "a");
        await kvas.Flush();

        // act
        await using var kvas2 = CreateKvas(services);

        // assert
        (await kvas2.Get<string>("a")).Should().BeNull();
        await kvas2.Set("b", "b"); // Must not throw
    }

    [Fact]
    public async Task KeyedStoreIsInertBeforeActivateTest()
    {
        // arrange
        var services = CreateServices();
        await using var kvas = CreateKvas(services, requiresActivation: true);

        // act
        await kvas.Set("a", "a");

        // assert
        (await kvas.Get<string>("a")).Should().BeNull();
        Directory.EnumerateFileSystemEntries(_basePath.DirectoryPath).Should().BeEmpty();
    }

    [Fact]
    public async Task ActivateIsolatesEntriesByKeyTest()
    {
        // arrange
        var services = CreateServices();
        await using var kvas = CreateKvas(services, requiresActivation: true);
        await kvas.Activate("aaaa1111");
        await kvas.Set("a", "a");

        // act
        await kvas.Activate("bbbb2222");

        // assert
        (await kvas.Get<string>("a")).Should().BeNull();
    }

    [Fact]
    public async Task ReactivatingSameKeyKeepsEntriesTest()
    {
        // arrange
        var services = CreateServices();
        await using var kvas = CreateKvas(services, requiresActivation: true);
        await kvas.Activate("aaaa1111");
        await kvas.Set("a", "a");
        await kvas.Flush();

        // act
        await kvas.Deactivate(false);
        await kvas.Activate("aaaa1111");

        // assert
        (await kvas.Get<string>("a")).Should().Be("a");
    }

    [Fact]
    public async Task DeactivateWithClearDropsEntriesTest()
    {
        // arrange
        var services = CreateServices();
        await using var kvas = CreateKvas(services, requiresActivation: true);
        await kvas.Activate("aaaa1111");
        await kvas.Set("a", "a");
        await kvas.Flush();

        // act
        await kvas.Deactivate(true);
        await kvas.Activate("aaaa1111");

        // assert
        (await kvas.Get<string>("a")).Should().BeNull();
    }

    [Fact]
    public async Task ActivateWhileSuspendedDefersToResumeTest()
    {
        // arrange
        var services = CreateServices();
        await using var kvas = CreateKvas(services, requiresActivation: true);
        await kvas.Suspend();

        // act
        await kvas.Activate("aaaa1111");
        var whenSuspended = await kvas.Get<string>("a");
        kvas.Resume();
        await kvas.Set("a", "a");

        // assert
        whenSuspended.Should().BeNull();
        (await kvas.Get<string>("a")).Should().Be("a");
    }

    [Fact]
    public async Task SupersededSweepKeepsTheLiveFolderTest()
    {
        // arrange
        var services = CreateServices();
        await using var kvas = CreateKvas(services, requiresActivation: true);

        // act
        await kvas.Activate("aaaa1111");
        await kvas.Activate("bbbb2222");
        await kvas.Set("b", "b");

        // assert
        await WhenFolderNamesAre(["Store-bbbb2222"]);
        (await kvas.Get<string>("b")).Should().Be("b");
    }

    [Fact]
    public async Task ActivateSweepsStaleKeyFoldersTest()
    {
        // arrange
        var services = CreateServices();
        await using var kvas = CreateKvas(services, requiresActivation: true);
        await kvas.Activate("aaaa1111");
        await kvas.Set("a", "a");
        await kvas.Flush();

        // act
        await kvas.Activate("bbbb2222");

        // assert
        await WhenFolderNamesAre(["Store-bbbb2222"]);
    }

    // Private methods

    private async Task WhenFolderNamesAre(string[] expected)
    {
        // The sweep runs in the background so switching away stays off the session-switch path
        var endsAt = CpuTimestamp.Now + TimeSpan.FromSeconds(5);
        while (true) {
            var names = Directory.EnumerateDirectories(_basePath.DirectoryPath)
                .Select(x => ((FilePath)x).FileName.Value)
                .ToArray();
            if (names.OrderBy(x => x).SequenceEqual(expected.OrderBy(x => x)))
                return;
            if (endsAt.Elapsed >= TimeSpan.Zero) {
                names.Should().BeEquivalentTo(expected);
                return;
            }

            await Task.Delay(50);
        }
    }

    private KvasarKvas CreateKvas(
        IServiceProvider services,
        string version = "1.0",
        bool requiresActivation = false)
        => new(new KvasarKvas.Options() {
            BasePath = _basePath,
            EncryptionKey = Key,
            Version = version,
            PageSize = 4 * 1024,
            PageCacheBytes = 1024 * 1024,
            FlushDelay = TimeSpan.FromMilliseconds(10),
            RequiresActivation = requiresActivation,
        }, services);

    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddFusion();
        return services.BuildServiceProvider();
    }
}
