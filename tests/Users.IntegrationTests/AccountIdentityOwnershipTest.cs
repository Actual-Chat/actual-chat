using ActualChat.Testing.Host;
using ActualLab.Resilience;
using ActualLab.Versioning;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public sealed class AccountIdentityOwnershipTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly IRetryPolicy UpdateRetryPolicy =
        new RetryPolicy(6, RetryDelaySeq.Exp(0.05, 1, multiplier: 2)) {
            RetryOn = (e, _) => e is VersionMismatchException,
        };

    private IAccountsBackend AccountsBackend => AppHost.Services.GetRequiredService<IAccountsBackend>();

    [Theory]
    [InlineData(AuthSchema.Email, "Email")]
    [InlineData(AuthSchema.Phone, "Phone number")]
    public async Task BackendUpdateShouldRejectAnotherAccountsIdentity(string schema, string target)
    {
        // arrange
        await using var ownerTester = AppHost.NewWebClientTester(Out);
        await using var otherTester = AppHost.NewWebClientTester(Out);
        var owner = await ownerTester.SignInAsUniqueAlice();
        var other = await otherTester.SignInAsUniqueBob();
        var identity = new UserIdentity(schema, UniqueNames.Name("identity"));
        await UpdateWithRetry(owner.Id, a => a.WithIdentity(identity));

        // act
        var update = () => UpdateWithRetry(other.Id, a => a.WithIdentity(identity));

        // assert
        await update.Should().ThrowAsync<Exception>()
            .WithMessage($"{target} has already been taken by another account.");
        (await AccountsBackend.GetIdByUserIdentity(identity, default)).Should().Be(owner.Id);
    }

    // Private methods

    private Task<Unit> UpdateWithRetry(UserId userId, Func<AccountFull, AccountFull> updater)
        // Background writers (ContactGreeter's greeting) bump a fresh account's version at any moment
        => UpdateRetryPolicy.Apply(async ct => {
            var account = await AccountsBackend.Get(userId, ct).Require();
            var command = new AccountsBackend_Update(updater(account), account.Version);
            await Commander.Call(command, ct);
            return default(Unit);
        });
}
