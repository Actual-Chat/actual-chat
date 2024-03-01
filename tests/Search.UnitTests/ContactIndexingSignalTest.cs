using System.Diagnostics;
using ActualChat.Search.Module;
using Microsoft.Extensions.Configuration;

namespace ActualChat.Search.UnitTests;

public class ContactIndexingSignalTest : IAsyncLifetime
{
    private ServiceProvider _services = null!;
    private ContactIndexingSignal _sut = null!;

    public Task InitializeAsync()
    {
        var config = new ConfigurationBuilder().AddInMemory(
                ($"{nameof(SearchSettings)}:{nameof(SearchSettings.ContactIndexingDelay)}", "00:00:00.02"),
                ($"{nameof(SearchSettings)}:{nameof(SearchSettings.ContactIndexingSignalInterval)}", "00:00:00.01")
                ).Build();
        var services = new ServiceCollection().AddSingleton(config)
            .AddSingleton<IConfiguration>(config)
            .AddSettings<SearchSettings>();
        services.AddFusion();
        _services = services.BuildServiceProvider();

        _sut = new ContactIndexingSignal(_services);


        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _sut.DisposeSilentlyAsync();
        await _services.DisposeSilentlyAsync().AsTask();
    }

    [Fact]
    public async Task ShouldSetWithDelay()
    {
        var sw = Stopwatch.StartNew();
        _sut.SetDelayed();
        await WhenSet();
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(0.5));
    }

    [Fact]
    public async Task ShouldNotSignalAfterReset()
    {
        _sut.SetDelayed();
        await WhenSet();
        _sut.Reset();
        Func<Task> action = () => WhenSet();
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ShouldSetMultiple()
    {
        _sut.SetDelayed();
        await WhenSet();
        _sut.Reset();

        var sw = Stopwatch.StartNew();
        _sut.SetDelayed();
        await WhenSet();
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(0.5));
    }

    private Task WhenSet(TimeSpan? timeout = null)
        => _sut.WhenSet(timeout ?? TimeSpan.FromSeconds(0.5), CancellationToken.None);
}
