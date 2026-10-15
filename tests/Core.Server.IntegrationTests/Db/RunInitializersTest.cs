using ActualChat.Testing.Host;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Core.Server.IntegrationTests.Db;

public sealed class RunInitializersTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(RunInitializersTest)}", TestAppHostOptions.None, @out)
{
    [Fact(Timeout = 60_000)]
    public async Task RunInitializersShouldPropagateDbInitializerFailure()
    {
        // arrange
        await using var host = await NewAppHost(o => o with {
            ConfigureServices = (_, services) => {
                services.RemoveAll<IDbInitializer>();
                services.AddSingleton<IDbInitializer, FailingDbInitializer>();
            },
        });

        // act
        var act = () => host.RunInitializers();

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            "a failed migration must stop the host instead of letting it serve the old schema")
            .WithMessage(FailingDbInitializer.Error);
    }

    // Nested types

    private sealed class FailingDbInitializer(IServiceProvider services) : IDbInitializer
    {
        public const string Error = "Migration failed";

        public IServiceProvider Services { get; } = services;
        public Dictionary<IDbInitializer, Task> RunningTasks { get; set; } = null!;
        public bool ShouldRepairData => false;
        public bool ShouldVerifyData => false;

        public Task InitializeSchema(CancellationToken cancellationToken)
            => Task.FromException(new InvalidOperationException(Error));

        public Task InitializeData(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RepairData(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task VerifyData(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
