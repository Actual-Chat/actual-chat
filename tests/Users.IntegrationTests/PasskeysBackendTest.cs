using System.Security.Cryptography;
using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class PasskeysBackendTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IPasskeysBackend Backend => AppHost.Services.GetRequiredService<IPasskeysBackend>();
    private IAccountsBackend AccountsBackend => AppHost.Services.GetRequiredService<IAccountsBackend>();

    [Fact]
    public async Task CreateShouldAddIdentityAndListThePasskey()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var credential = NewCredential(account.Id);

        // act
        await Commander.Call(new PasskeysBackend_Change(account.Id, credential.Id, Change.Create(credential)));

        // assert
        var identity = UserIdentityExt.NewPasskeyIdentity(credential.Id);
        await ComputedTest.When(async ct => {
            var listed = await Backend.List(account.Id, ct);
            listed.Should().ContainSingle(x => x.Id == credential.Id);
            (await AccountsBackend.GetIdByUserIdentity(identity, ct)).Should().Be(account.Id);
            var full = await AccountsBackend.Get(account.Id, ct);
            full!.Identities.Keys.Should().Contain(identity, "the passkey must be an account identity");
        });
    }

    [Fact]
    public async Task RemoveShouldDropIdentityAndPasskey()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var credential = NewCredential(account.Id);
        await Commander.Call(new PasskeysBackend_Change(account.Id, credential.Id, Change.Create(credential)));

        // act
        await Commander.Call(new PasskeysBackend_Change(account.Id, credential.Id, Change.Remove<PasskeyCredential>()));

        // assert
        var identity = UserIdentityExt.NewPasskeyIdentity(credential.Id);
        await ComputedTest.When(async ct => {
            (await Backend.Get(account.Id, credential.Id, ct)).Should().BeNull();
            (await Backend.List(account.Id, ct)).Should().BeEmpty();
            (await AccountsBackend.GetIdByUserIdentity(identity, ct)).Should().BeNull();
        });
    }

    [Fact]
    public async Task UpdateShouldPersistCounterAndName()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var credential = NewCredential(account.Id);
        await Commander.Call(new PasskeysBackend_Change(account.Id, credential.Id, Change.Create(credential)));
        var updated = credential with { SignCount = 7, Name = "Laptop", IsBackedUp = true };

        // act
        await Commander.Call(new PasskeysBackend_Change(account.Id, credential.Id, Change.Update(updated)));

        // assert
        await ComputedTest.When(async ct => {
            var stored = await Backend.Get(account.Id, credential.Id, ct);
            stored.Should().NotBeNull();
            stored!.SignCount.Should().Be(7);
            stored.Name.Should().Be("Laptop");
            stored.IsBackedUp.Should().BeTrue();
        });
    }

    [Fact]
    public async Task CreateShouldRefuseCredentialOwnedByAnotherAccount()
    {
        // arrange
        await using var tester1 = AppHost.NewWebClientTester(Out);
        await using var tester2 = AppHost.NewWebClientTester(Out);
        var alice = await tester1.SignInAsUniqueAlice();
        var bob = await tester2.SignInAsUniqueBob();
        var credential = NewCredential(alice.Id);
        await Commander.Call(new PasskeysBackend_Change(alice.Id, credential.Id, Change.Create(credential)));

        // act
        var act = () => Commander.Call(
            new PasskeysBackend_Change(bob.Id, credential.Id, Change.Create(credential with { UserId = bob.Id })));

        // assert
        await act.Should().ThrowAsync<Exception>("a credential id belongs to exactly one account");
    }

    private static PasskeyCredential NewCredential(UserId userId)
        => new(UniqueNames.Random(32), userId) {
            UserHandle = RandomNumberGenerator.GetBytes(32),
            PublicKey = RandomNumberGenerator.GetBytes(77),
            Aaguid = Guid.NewGuid(),
            Transports = "internal",
            IsBackupEligible = true,
            Name = "Test passkey",
            CreatedAt = Moment.Now,
        };
}
