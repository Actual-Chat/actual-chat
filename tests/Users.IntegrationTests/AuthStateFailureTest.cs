using ActualChat.Testing.Host;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.IntegrationTests;

// Simulates the rolling-deploy window, where a host runs against a schema it doesn't match: reads
// keep working, but every command that stores an operation fails. Renaming command_data away is
// the "new pod, old schema" half of it; the 15.0 migration dropping command_json is what the same
// window looks like to a pod still running 14.x. Either way AuthHelper.UpdateAuthState throws for
// every request, including ones that have nothing to do with signing in.
[Trait("Category", "Slow")]
public sealed class AuthStateFailureTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(AuthStateFailureTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 120_000)]
    public async Task ABrokenOperationsSchemaShouldNotLookLikeAFailedSignIn()
    {
        await using var h = await NewAppHost();
        var authHelper = h.Services.GetRequiredService<AuthHelper>();

        // arrange
        var healthy = await authHelper.UpdateAuthState(NewHttpContext(h));
        healthy.IsUnavailable.Should().BeFalse();
        healthy.CloseFlow.Should().BeNull();

        // act
        await RenameOperationsColumn(h, "command_data", "command_data_v14");
        var unavailable = await authHelper.UpdateAuthState(NewHttpContext(h));

        // assert
        unavailable.IsUnavailable.Should().BeTrue();
        unavailable.Error.Should().NotBeNullOrEmpty();
        // The request was for an ordinary page, so there is no flow to report a failure through -
        // and inventing one told the visitor their sign-in failed and to close the tab.
        unavailable.CloseFlow.Should().BeNull();
        Out.WriteLine($"Error: {unavailable.Error}");

        // act & assert: the window closes on its own once the schema matches again
        await RenameOperationsColumn(h, "command_data_v14", "command_data");
        var recovered = await authHelper.UpdateAuthState(NewHttpContext(h));
        recovered.IsUnavailable.Should().BeFalse();
    }

    // Private methods

    private static DefaultHttpContext NewHttpContext(TestAppHost h)
        => new() {
            RequestServices = h.Services,
            Request = { Method = "GET", Path = "/" },
        };

    private static async Task RenameOperationsColumn(TestAppHost h, string from, string to)
    {
        var dbHub = h.Services.GetRequiredService<DbHub<UsersDbContext>>();
        var dbContext = await dbHub.CreateDbContext(readWrite: true, CancellationToken.None);
        await using var _ = dbContext.ConfigureAwait(false);
#pragma warning disable EF1002
        await dbContext.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE _operations RENAME COLUMN {from} TO {to}", CancellationToken.None);
#pragma warning restore EF1002
    }
}
