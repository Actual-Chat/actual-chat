using System.Security.Claims;
using ActualChat.Testing.Host;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class AccountUpdateTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IAccounts Accounts => AppHost.Services.GetRequiredService<IAccounts>();
    private IAccountsBackend AccountsBackend => AppHost.Services.GetRequiredService<IAccountsBackend>();
    private DbHub<UsersDbContext> DbHub => AppHost.Services.GetRequiredService<DbHub<UsersDbContext>>();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    // Helper: waits for GetOwn to reflect the update
    private Task<AccountFull> WhenOwnAccountUpdated(Func<AccountFull, bool> predicate)
    {
        AccountFull result = null!;
        return ComputedTest.When(async ct => {
            result = await Accounts.GetOwn(Tester.Session, ct);
            predicate(result).Should().BeTrue();
        }).ContinueWith(_ => result, TaskScheduler.Current);
    }

    // Tests for Accounts.OnUpdate (public API) - should preserve Claims and Identities

    [Fact]
    public async Task AccountsUpdate_ShouldPreserveClaimsWhenNotProvided()
    {
        // Arrange - create account with claims
        var account = await Tester.SignInAsUniqueBob();
        account.Claims.Should().NotBeEmpty();
        var originalClaims = account.Claims;

        // Act - update account with empty claims (simulating old app behavior)
        var accountWithEmptyClaims = account with {
            Name = account.Name + " Updated",
            Claims = new ApiMap<string, string>(),
        };
        var updateCommand = new Accounts_Update(Tester.Session, accountWithEmptyClaims, null);
        await Tester.Commander.Call(updateCommand);

        // Assert - claims should be preserved
        var updatedAccount = await WhenOwnAccountUpdated(a => a.Name.EndsWith(" Updated"));
        updatedAccount.Claims.Should().BeEquivalentTo(originalClaims);
    }

    [Fact]
    public async Task AccountsUpdate_ShouldPreserveIdentitiesWhenNotProvided()
    {
        // Arrange - create account with identities
        var account = await Tester.SignInAsUniqueBob();
        account.Identities.Should().NotBeEmpty();
        var originalIdentities = account.Identities;

        // Act - update account with empty identities (simulating old app behavior)
        var accountWithEmptyIdentities = account with {
            Name = account.Name + " Updated",
            Identities = new ApiMap<UserIdentity, string>(),
        };
        var updateCommand = new Accounts_Update(Tester.Session, accountWithEmptyIdentities, null);
        await Tester.Commander.Call(updateCommand);

        // Assert - identities should be preserved
        var updatedAccount = await WhenOwnAccountUpdated(a => a.Name.EndsWith(" Updated"));
        updatedAccount.Identities.Should().BeEquivalentTo(originalIdentities);
    }

    [Fact]
    public async Task AccountsUpdate_ShouldPreserveBothClaimsAndIdentitiesWhenNotProvided()
    {
        // Arrange - create account with claims and identities
        var account = await Tester.SignInAsUniqueBob();
        account.Claims.Should().NotBeEmpty();
        account.Identities.Should().NotBeEmpty();
        var originalClaims = account.Claims;
        var originalIdentities = account.Identities;

        // Act - update account with empty claims and identities (simulating old app behavior)
        var accountWithEmptyData = account with {
            Name = account.Name + " Updated",
            Claims = new ApiMap<string, string>(),
            Identities = new ApiMap<UserIdentity, string>(),
        };
        var updateCommand = new Accounts_Update(Tester.Session, accountWithEmptyData, null);
        await Tester.Commander.Call(updateCommand);

        // Assert - both claims and identities should be preserved
        var updatedAccount = await WhenOwnAccountUpdated(a => a.Name.EndsWith(" Updated"));
        updatedAccount.Claims.Should().BeEquivalentTo(originalClaims);
        updatedAccount.Identities.Should().BeEquivalentTo(originalIdentities);
    }

    [Fact]
    public async Task AccountsUpdate_ShouldAllowUpdatingOtherProperties()
    {
        // Arrange - create account
        var account = await Tester.SignInAsUniqueBob();
        var newName = "New Name " + Guid.NewGuid().ToString("N")[..8];
        var newTimeZone = "America/New_York";

        // Act - update other properties
        var updatedAccountData = account with {
            Name = newName,
            TimeZone = newTimeZone,
            SyncContacts = true,
        };
        var updateCommand = new Accounts_Update(Tester.Session, updatedAccountData, null);
        await Tester.Commander.Call(updateCommand);

        // Assert - other properties should be updated
        var updatedAccount = await WhenOwnAccountUpdated(a => a.Name == newName);
        updatedAccount.TimeZone.Should().Be(newTimeZone);
        updatedAccount.SyncContacts.Should().BeTrue();
    }

    [Fact]
    public async Task IgnoresServerOwnedFieldsOnUpdate()
    {
        // arrange
        var account = await Tester.SignInAsUniqueBob();
        await Commander.Call(new AccountsBackend_Update(account with { IsGreetingCompleted = true }, null));
        await ComputedTest.When(async ct => {
            var a = await Accounts.GetOwn(Tester.Session, ct);
            a.IsGreetingCompleted.Should().BeTrue();
        });
        account = await Accounts.GetOwn(Tester.Session, default);
        var createdAt = account.CreatedAt;
        var newName = UniqueNames.Name("Derived");

        // act
        var updatedAccountData = account with {
            Name = newName,
            CreatedAt = createdAt - TimeSpan.FromDays(365),
            IsGreetingCompleted = false,
        };
        var updateCommand = new Accounts_Update(Tester.Session, updatedAccountData, null);
        await Tester.Commander.Call(updateCommand);

        // assert
        var updatedAccount = await WhenOwnAccountUpdated(a => a.Name == newName);
        updatedAccount.CreatedAt.Should().Be(createdAt);
        updatedAccount.IsGreetingCompleted.Should().BeTrue();
    }

    // Tests for AccountsBackend.OnUpdate (internal API) - should allow updating Claims and Identities

    [Fact]
    public async Task AccountsBackendUpdate_ShouldUpdateClaims()
    {
        // Arrange - create account
        var account = await Tester.SignInAsUniqueBob();
        var newClaimValue = "TestValue_" + Guid.NewGuid().ToString("N")[..8];

        // Act - update claims via backend
        var accountWithNewClaim = account.WithClaim("custom_claim", newClaimValue);
        var updateCommand = new AccountsBackend_Update(accountWithNewClaim, null);
        await Commander.Call(updateCommand);

        // Assert - claims should be updated
        await ComputedTest.When(async ct => {
            var updatedAccount = await AccountsBackend.Get(account.Id, ct);
            updatedAccount.Should().NotBeNull();
            updatedAccount!.Claims["custom_claim"].Should().Be(newClaimValue);
        });
    }

    [Fact]
    public async Task AccountsBackendUpdate_ShouldUpdateIdentities()
    {
        // Arrange - create account
        var account = await Tester.SignInAsUniqueBob();
        var newIdentity = new UserIdentity("test_provider", "test_id_" + Guid.NewGuid().ToString("N")[..8]);

        // Act - update identities via backend
        var accountWithNewIdentity = account.WithIdentity(newIdentity, "test_secret");
        var updateCommand = new AccountsBackend_Update(accountWithNewIdentity, null);
        await Commander.Call(updateCommand);

        // Assert - identities should be updated
        await ComputedTest.When(async ct => {
            var updatedAccount = await AccountsBackend.Get(account.Id, ct);
            updatedAccount.Should().NotBeNull();
            updatedAccount!.Identities.ContainsKey(newIdentity).Should().BeTrue();
            updatedAccount.Identities[newIdentity].Should().Be("test_secret");
        });
    }

    [Fact]
    public async Task AccountsBackendUpdate_ShouldAllowClearingClaims()
    {
        // Arrange - create account with custom claim
        var account = await Tester.SignInAsUniqueBob();
        var accountWithClaim = account.WithClaim("custom_claim", "value");
        var setupCommand = new AccountsBackend_Update(accountWithClaim, null);
        await Commander.Call(setupCommand);

        // Verify claim exists
        await ComputedTest.When(async ct => {
            var a = await AccountsBackend.Get(account.Id, ct);
            a!.Claims.ContainsKey("custom_claim").Should().BeTrue();
        });

        // Act - clear claims via backend (set to empty)
        var accountAfterSetup = await AccountsBackend.Get(account.Id, default);
        var accountWithEmptyClaims = accountAfterSetup! with { Claims = new ApiMap<string, string>() };
        var updateCommand = new AccountsBackend_Update(accountWithEmptyClaims, null);
        await Commander.Call(updateCommand);

        // Assert - claims should be empty
        await ComputedTest.When(async ct => {
            var updatedAccount = await AccountsBackend.Get(account.Id, ct);
            updatedAccount.Should().NotBeNull();
            updatedAccount!.Claims.Should().BeEmpty();
        });
    }

    // Tests for backward compatibility with old apps

    [Fact]
    public async Task AccountsUpdate_OldAppScenario_ShouldNotWipeClaimsOrIdentities()
    {
        // Arrange - create account with claims and identities
        var account = await Tester.SignInAsUniqueBob();

        account.Claims.Should().NotBeEmpty();
        account.Identities.Should().NotBeEmpty();
        var originalClaimsCount = account.Claims.Count;
        var originalIdentitiesCount = account.Identities.Count;

        // Act - simulate old app update (sends minimal data with empty Claims and Identities)
        var oldAppUpdate = new AccountFull(account.Id, account.Version) {
            Name = "Updated Name",
            Status = account.Status,
            Email = account.Email,
            Phone = account.Phone,
        };
        var updateCommand = new Accounts_Update(Tester.Session, oldAppUpdate, null);
        await Tester.Commander.Call(updateCommand);

        // Assert - claims and identities should be preserved
        var updatedAccount = await WhenOwnAccountUpdated(a => a.Name == "Updated Name");
        updatedAccount.Claims.Count.Should().Be(originalClaimsCount);
        updatedAccount.Identities.Count.Should().Be(originalIdentitiesCount);
    }

    [Fact]
    public async Task AccountsUpdate_ShouldIgnoreProvidedClaimsAndIdentities()
    {
        // Arrange - create account
        var account = await Tester.SignInAsUniqueBob();
        var originalClaims = account.Claims;
        var originalIdentities = account.Identities;

        // Act - try to update claims and identities via public API
        var accountWithNewClaimsAndIdentities = account
            .WithClaim("malicious_claim", "should_not_appear")
            .WithIdentity(new UserIdentity("malicious_provider", "malicious_id"));
        var updateCommand = new Accounts_Update(Tester.Session, accountWithNewClaimsAndIdentities, null);
        await Tester.Commander.Call(updateCommand);

        // Assert - claims and identities should remain unchanged (original values preserved)
        await ComputedTest.When(async ct => {
            var updatedAccount = await Accounts.GetOwn(Tester.Session, ct);
            updatedAccount.Claims.Should().BeEquivalentTo(originalClaims);
            updatedAccount.Identities.Should().BeEquivalentTo(originalIdentities);
            updatedAccount.Claims.ContainsKey("malicious_claim").Should().BeFalse();
        });
    }

    // Tests for version checking

    [Fact]
    public async Task AccountsUpdate_ShouldIncrementVersion()
    {
        // Arrange
        var account = await Tester.SignInAsUniqueBob();
        var originalVersion = account.Version;

        // Act
        var updatedAccountData = account with { Name = account.Name + " V2" };
        var updateCommand = new Accounts_Update(Tester.Session, updatedAccountData, null);
        await Tester.Commander.Call(updateCommand);

        // Assert
        await ComputedTest.When(async ct => {
            var updatedAccount = await Accounts.GetOwn(Tester.Session, ct);
            updatedAccount.Version.Should().BeGreaterThan(originalVersion);
        });
    }

    [Fact]
    public async Task AccountsUpdate_WithExpectedVersion_ShouldSucceed()
    {
        // Arrange
        var account = await Tester.SignInAsUniqueBob();

        // Act
        var updatedAccountData = account with { Name = account.Name + " WithVersion" };
        var updateCommand = new Accounts_Update(Tester.Session, updatedAccountData, account.Version);
        await Tester.Commander.Call(updateCommand);

        // Assert
        var updatedAccount = await WhenOwnAccountUpdated(a => a.Name.EndsWith(" WithVersion"));
        updatedAccount.Name.Should().Be(account.Name + " WithVersion");
    }

    [Fact]
    public async Task AccountsUpdate_WithWrongExpectedVersion_ShouldFail()
    {
        // Arrange
        var account = await Tester.SignInAsUniqueBob();

        // Act & Assert
        var updatedAccountData = account with { Name = account.Name + " WrongVersion" };
        var updateCommand = new Accounts_Update(Tester.Session, updatedAccountData, account.Version + 100);

        var act = () => Tester.Commander.Call(updateCommand);
        await act.Should().ThrowAsync<Exception>();
    }

    // Database verification tests

    [Fact]
    public async Task AccountsUpdate_ShouldPreserveClaimsInDatabase()
    {
        // Arrange - create account with claims
        var account = await Tester.SignInAsUniqueBob();
        account.Claims.Should().NotBeEmpty();
        var originalClaims = account.Claims;

        // Act - update via public API with empty claims
        var accountWithEmptyClaims = account with {
            Name = account.Name + " DbTest",
            Claims = new ApiMap<string, string>(),
        };
        var updateCommand = new Accounts_Update(Tester.Session, accountWithEmptyClaims, null);
        await Tester.Commander.Call(updateCommand);

        // Wait for update to propagate
        await WhenOwnAccountUpdated(a => a.Name.EndsWith(" DbTest"));

        // Assert - verify claims are preserved in database
        var dbAccount = await GetDbAccount(account.Id);
        dbAccount.Claims.Should().NotBeEmpty();
        dbAccount.Claims.Should().BeEquivalentTo(originalClaims.UnorderedItems.ToDictionary(p => p.Key, p => p.Value));
    }

    [Fact]
    public async Task AccountsUpdate_ShouldPreserveIdentitiesInDatabase()
    {
        // Arrange - create account with identities
        var account = await Tester.SignInAsUniqueBob();
        account.Identities.Should().NotBeEmpty();
        var originalIdentityCount = account.Identities.Count;

        // Act - update via public API with empty identities
        var accountWithEmptyIdentities = account with {
            Name = account.Name + " DbTest",
            Identities = new ApiMap<UserIdentity, string>(),
        };
        var updateCommand = new Accounts_Update(Tester.Session, accountWithEmptyIdentities, null);
        await Tester.Commander.Call(updateCommand);

        // Wait for update to propagate
        await WhenOwnAccountUpdated(a => a.Name.EndsWith(" DbTest"));

        // Assert - verify identities are preserved in database
        var dbAccount = await GetDbAccountWithIdentities(account.Id);
        dbAccount.Identities.Should().HaveCount(originalIdentityCount);
    }

    // Tests for sign-in preserving existing data

    [Fact]
    public async Task SignIn_ShouldPreservePhoneWhenReSigningIn()
    {
        // Arrange - create account with phone
        var phone = UniqueNames.Phone();
        var account = new AccountFull(UniqueNames.Name("User"))
            .WithClaim(ClaimTypes.GivenName, "Test")
            .WithPhoneIdentity(phone);
        account = await Tester.SignIn(account);

        account.Phone.Should().Be(phone);
        account.Identities.Should().NotBeEmpty();
        var originalIdentitiesCount = account.Identities.Count;

        // Act - sign out and sign in again (simulating OAuth sign-in without phone)
        await Tester.SignOut();
        var signedInAccount = await Tester.SignIn(account);

        // Assert - phone and identities should be preserved
        signedInAccount.Phone.Should().Be(phone);
        signedInAccount.Identities.Count.Should().Be(originalIdentitiesCount);
    }

    [Fact]
    public async Task SignIn_ShouldPreserveEmailWhenReSigningIn()
    {
        // Arrange - create account with email
        var email = $"test_{Guid.NewGuid():N}@example.com";
        var account = new AccountFull(UniqueNames.Name("User"))
            .WithClaim(ClaimTypes.GivenName, "Test")
            .WithClaim(ClaimTypes.Email, email);
        account = await Tester.SignIn(account);

        account.Email.Should().NotBeEmpty();

        // Act - sign out and sign in again
        await Tester.SignOut();
        var signedInAccount = await Tester.SignIn(account);

        // Assert - email should be preserved
        signedInAccount.Email.Should().Be(email);
    }

    [Fact]
    public async Task SignIn_ShouldMergeIdentitiesNotReplace()
    {
        // Arrange - create account with phone identities
        var phone = UniqueNames.Phone();
        var account = new AccountFull(UniqueNames.Name("User"))
            .WithClaim(ClaimTypes.GivenName, "Test")
            .WithPhoneIdentity(phone);
        account = await Tester.SignIn(account);

        var originalIdentitiesCount = account.Identities.Count;
        originalIdentitiesCount.Should().BeGreaterThan(0);

        // Act - sign out and sign in with a different identity
        await Tester.SignOut();
        var accountWithNewIdentity = account.WithIdentity(new UserIdentity(UserIdentity.InternalSchema, UniqueNames.Random()));
        var signedInAccount = await Tester.SignIn(accountWithNewIdentity);

        // Assert - identities should be merged (original + new), not replaced
        signedInAccount.Identities.Count.Should().BeGreaterThanOrEqualTo(originalIdentitiesCount);
        // Phone identities should still be present
        signedInAccount.Identities.GetPhoneIdentities().Should().NotBeEmpty();
    }

    [Fact]
    public async Task SignIn_ShouldPreserveTimeZoneWhenReSigningIn()
    {
        // Arrange - create account and set timezone
        var account = await Tester.SignInAsUniqueBob();
        var timeZone = "America/New_York";

        var accountWithTimeZone = account with { TimeZone = timeZone };
        var updateCommand = new Accounts_Update(Tester.Session, accountWithTimeZone, null);
        await Tester.Commander.Call(updateCommand);

        var updatedAccount = await WhenOwnAccountUpdated(a => a.TimeZone == timeZone);

        // Act - sign out and sign in again
        await Tester.SignOut();
        var signedInAccount = await Tester.SignIn(updatedAccount);

        // Assert - timezone should be preserved
        signedInAccount.TimeZone.Should().Be(timeZone);
    }

    [Fact]
    public async Task SignIn_ShouldPreserveSyncContactsWhenReSigningIn()
    {
        // Arrange - create account and enable sync contacts
        var account = await Tester.SignInAsUniqueBob();

        var accountWithSyncContacts = account with { SyncContacts = true };
        var updateCommand = new Accounts_Update(Tester.Session, accountWithSyncContacts, null);
        await Tester.Commander.Call(updateCommand);

        var updatedAccount = await WhenOwnAccountUpdated(a => a.SyncContacts);

        // Act - sign out and sign in again
        await Tester.SignOut();
        var signedInAccount = await Tester.SignIn(updatedAccount);

        // Assert - sync contacts setting should be preserved
        signedInAccount.SyncContacts.Should().BeTrue();
    }

    [Fact]
    public async Task SignIn_ShouldPreserveIsGreetingCompletedWhenReSigningIn()
    {
        // Arrange - create account with greeting completed
        var account = await Tester.SignInAsUniqueBob();

        // Mark greeting as completed via backend (since it's not updateable via public API)
        var accountWithGreeting = account with { IsGreetingCompleted = true };
        var backendUpdateCommand = new AccountsBackend_Update(accountWithGreeting, null);
        await Commander.Call(backendUpdateCommand);

        await ComputedTest.When(async ct => {
            var a = await Accounts.GetOwn(Tester.Session, ct);
            a.IsGreetingCompleted.Should().BeTrue();
        });

        // Act - sign out and sign in again
        var updatedAccount = await Accounts.GetOwn(Tester.Session, default);
        await Tester.SignOut();
        var signedInAccount = await Tester.SignIn(updatedAccount);

        // Assert - greeting completed should be preserved
        signedInAccount.IsGreetingCompleted.Should().BeTrue();
    }

    // Helpers

    private async Task<DbAccount> GetDbAccount(UserId userId)
    {
        var dbContext = await DbHub.CreateDbContext(default);
        await using var _ = dbContext;
        return await dbContext.Accounts.FirstAsync(a => a.Id == userId.Value);
    }

    private async Task<DbAccount> GetDbAccountWithIdentities(UserId userId)
    {
        var dbContext = await DbHub.CreateDbContext(default);
        await using var _ = dbContext;
        return await dbContext.Accounts
            .Include(a => a.Identities)
            .FirstAsync(a => a.Id == userId.Value);
    }
}
