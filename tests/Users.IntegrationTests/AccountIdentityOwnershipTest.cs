using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public sealed class AccountIdentityOwnershipTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
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
        var backend = AppHost.Services.GetRequiredService<IAccountsBackend>();
        var identity = new UserIdentity(schema, UniqueNames.Name("identity"));

        owner = (await backend.Get(owner.Id, default)).Require();
        await Commander.Call(new AccountsBackend_Update(
            owner.WithIdentity(identity), owner.Version));

        other = (await backend.Get(other.Id, default)).Require();
        var updateCommand = new AccountsBackend_Update(
            other.WithIdentity(identity), other.Version);

        // act
        var update = () => Commander.Call(updateCommand);

        // assert
        await update.Should().ThrowAsync<Exception>()
            .WithMessage($"{target} has already been taken by another account.");
        (await backend.GetIdByUserIdentity(identity, default)).Should().Be(owner.Id);
    }
}
