using ActualChat.Db;
using ActualChat.Testing.Host;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class AccountFormatVersionMigrationTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

    private WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IAccounts Accounts => AppHost.Services.GetRequiredService<IAccounts>();
    private IAccountsBackend AccountsBackend => AppHost.Services.GetRequiredService<IAccountsBackend>();
    private DbHub<UsersDbContext> DbHub => AppHost.Services.GetRequiredService<DbHub<UsersDbContext>>();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task NewAccountsShouldHaveFormatVersion2()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // Arrange & Act
        var account = await Tester.SignInAsUniqueBob(cts.Token);

        // Assert
        account.FormatVersion.Should().Be(2);
    }

    [Fact]
    public async Task NewAccountsShouldHaveIdentitiesInDbAccount()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // Arrange & Act
        var account = await Tester.SignInAsUniqueBob(cts.Token);

        // Assert
        account.Identities.Should().NotBeEmpty();

        // Verify identities are in DbAccountIdentity table
        var dbAccount = await GetDbAccountWithIdentities(account.Id, cts.Token);
        dbAccount.Identities.Should().NotBeEmpty();
        dbAccount.Identities.Should().HaveCount(account.Identities.Count);
    }

    [FlakyFact("AY: Direct DB write sometimes triggers deadlocks", 5)]
    public async Task ShouldReadAccountWithFormatVersion0()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // Arrange - create account with FormatVersion=1
        var account = await Tester.SignInAsUniqueBob(cts.Token);
        account.FormatVersion.Should().Be(2);

        // Downgrade to FormatVersion=0 to simulate legacy data
        await SetFormatVersionToZero(account.Id, cts.Token);

        // Act - read the account
        var readAccount = await Accounts.GetOwn(Tester.Session, cts.Token);

        // Assert - should still read correctly (using DbUser for Name/Claims)
        readAccount.Should().NotBeNull();
        readAccount!.Id.Should().Be(account.Id);
        readAccount.FormatVersion.Should().Be(0);
        readAccount.Name.Should().Be(account.Name);
    }

    [FlakyFact("AY: Direct DB write sometimes triggers deadlocks", 5)]
    public async Task ShouldUpgradeFormatVersionOnUpdate()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // Arrange - create account with FormatVersion=1
        var account = await Tester.SignInAsUniqueBob(cts.Token);
        account.FormatVersion.Should().Be(2);

        // Downgrade to FormatVersion=0 to simulate legacy data
        await SetFormatVersionToZero(account.Id, cts.Token);

        // Verify it's now 0
        var accountBeforeUpdate = await Accounts.GetOwn(Tester.Session, cts.Token);
        accountBeforeUpdate!.FormatVersion.Should().Be(0);

        // Act - update the account (this should trigger upgrade to FormatVersion=1)
        var updatedName = accountBeforeUpdate!.Name + " Updated";
        var updateCommand = new Accounts_Update(Tester.Session, accountBeforeUpdate with { Name = updatedName }, null);
        await Tester.Commander.Call(updateCommand, cts.Token);

        // Assert - FormatVersion should now be 1
        var accountAfterUpdate = await Accounts.GetOwn(Tester.Session, cts.Token);
        accountAfterUpdate.Should().NotBeNull();
        accountAfterUpdate!.FormatVersion.Should().Be(2);
        accountAfterUpdate.Name.Should().Be(updatedName);
    }

    [FlakyFact("AY: Direct DB write sometimes triggers deadlocks", 5)]
    public async Task ShouldSyncClaimsOnFormatVersionUpgrade()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // Arrange - create account with claims
        var account = await Tester.SignInAsUniqueBob(cts.Token);
        account.Claims.Should().NotBeEmpty();

        // Downgrade to FormatVersion=0 and clear Claims in DbAccount
        await SetFormatVersionToZeroAndClearClaims(account.Id, cts.Token);

        // Verify it's now 0 and claims are still readable (from DbUser)
        var accountBeforeUpdate = await Accounts.GetOwn(Tester.Session, cts.Token);
        accountBeforeUpdate!.FormatVersion.Should().Be(0);
        accountBeforeUpdate.Claims.Should().NotBeEmpty();

        // Act - update the account (this should trigger upgrade and sync claims)
        var updateCommand = new Accounts_Update(Tester.Session, accountBeforeUpdate, null);
        await Tester.Commander.Call(updateCommand, cts.Token);

        // Assert - FormatVersion should now be 1 and Claims should be in DbAccount
        var accountAfterUpdate = await Accounts.GetOwn(Tester.Session, cts.Token);
        accountAfterUpdate!.FormatVersion.Should().Be(2);
        accountAfterUpdate.Claims.Should().BeEquivalentTo(accountBeforeUpdate.Claims);

        // Verify Claims are actually in DbAccount
        var dbAccount = await GetDbAccount(account.Id, cts.Token);
        dbAccount.FormatVersion.Should().Be(2);
        dbAccount.Claims.Should().NotBeEmpty();
    }

    [FlakyFact("AY: Direct DB write sometimes triggers deadlocks", 5)]
    public async Task ShouldUpgradeFormatVersionOnSignIn()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // Arrange - create account
        var account = await Tester.SignInAsUniqueBob(cts.Token);
        account.FormatVersion.Should().Be(2);

        // Downgrade to FormatVersion=0
        await SetFormatVersionToZero(account.Id, cts.Token);
        await Tester.SignOut(cancellationToken: cts.Token);

        // Act - sign in again (should trigger upgrade)
        var accountAfterSignIn = await Tester.SignIn(account, cts.Token);

        // Assert - FormatVersion should now be 1
        accountAfterSignIn.FormatVersion.Should().Be(2);
    }

    [FlakyFact("AY: Direct DB write sometimes triggers deadlocks", 5)]
    public async Task ShouldReadIdentitiesFromDbUserWhenFormatVersion0()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // Arrange - create account with identities
        var account = await Tester.SignInAsUniqueBob(cts.Token);
        account.Identities.Should().NotBeEmpty();

        // Downgrade to FormatVersion=0 and clear identities in DbAccount
        await SetFormatVersionToZeroAndClearIdentities(account.Id, cts.Token);

        // Act - read the account
        var readAccount = await Accounts.GetOwn(Tester.Session, cts.Token);

        // Assert - should still have identities (read from DbUser)
        readAccount.Should().NotBeNull();
        readAccount!.FormatVersion.Should().Be(0);
        readAccount.Identities.Should().NotBeEmpty();
        readAccount.Identities.Count.Should().Be(account.Identities.Count);
    }

    [FlakyFact("AY: Direct DB write sometimes triggers deadlocks", 5)]
    public async Task ShouldSyncIdentitiesOnFormatVersionUpgrade()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // Arrange - create account with identities
        var account = await Tester.SignInAsUniqueBob(cts.Token);
        account.Identities.Should().NotBeEmpty();

        // Downgrade to FormatVersion=0 and clear identities in DbAccount
        await SetFormatVersionToZeroAndClearIdentities(account.Id, cts.Token);

        // Verify identities still readable from DbUser
        var accountBeforeUpdate = await Accounts.GetOwn(Tester.Session, cts.Token);
        accountBeforeUpdate!.Identities.Should().NotBeEmpty();

        // Act - update the account (should trigger upgrade and sync identities)
        var updateCommand = new Accounts_Update(Tester.Session, accountBeforeUpdate, null);
        await Tester.Commander.Call(updateCommand, cts.Token);

        // Assert - FormatVersion should now be 1 and identities should be in DbAccount
        var accountAfterUpdate = await Accounts.GetOwn(Tester.Session, cts.Token);
        accountAfterUpdate!.FormatVersion.Should().Be(2);
        accountAfterUpdate.Identities.Should().BeEquivalentTo(accountBeforeUpdate.Identities);

        // Verify identities are now in DbAccount
        var dbAccount = await GetDbAccountWithIdentities(account.Id, cts.Token);
        dbAccount.FormatVersion.Should().Be(2);
        dbAccount.Identities.Should().NotBeEmpty();
    }

    // Helpers

    private async Task<DbAccount> GetDbAccount(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken);
        await using var _ = dbContext;
        return await dbContext.Accounts.FirstAsync(a => a.Id == userId.Value, cancellationToken);
    }

    private async Task<DbAccount> GetDbAccountWithIdentities(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken);
        await using var _ = dbContext;
        return await dbContext.Accounts
            .Include(a => a.Identities)
            .FirstAsync(a => a.Id == userId.Value, cancellationToken);
    }

    private async Task SetFormatVersionToZero(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken);
        await using var _ = dbContext;
        await dbContext.Accounts.Lock(userId, cancellationToken);
        var dbAccount = await dbContext.Accounts.FirstAsync(a => a.Id == userId.Value, cancellationToken);
        dbAccount.FormatVersion = 0;
        await dbContext.SaveChangesAsync(cancellationToken);
        InvalidateAccount(userId);
    }

    private async Task SetFormatVersionToZeroAndClearClaims(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken);
        await using var _ = dbContext;
        await dbContext.Accounts.Lock(userId, cancellationToken);
        var dbAccount = await dbContext.Accounts.FirstAsync(a => a.Id == userId.Value, cancellationToken);
        dbAccount.FormatVersion = 0;
        dbAccount.Claims = ImmutableDictionary<string, string>.Empty;
        await dbContext.SaveChangesAsync(cancellationToken);
        InvalidateAccount(userId);
    }

    private async Task SetFormatVersionToZeroAndClearIdentities(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken);
        await using var _ = dbContext;
        await dbContext.Accounts.Lock(userId, cancellationToken);
        var dbAccount = await dbContext.Accounts
            .Include(a => a.Identities)
            .FirstAsync(a => a.Id == userId.Value, cancellationToken);
        dbAccount.FormatVersion = 0;
        dbAccount.Identities.Clear();
        await dbContext.SaveChangesAsync(cancellationToken);
        InvalidateAccount(userId);
    }

    private void InvalidateAccount(UserId userId)
    {
        using (Invalidation.Begin())
            _ = AccountsBackend.Get(userId, default);
    }
}
