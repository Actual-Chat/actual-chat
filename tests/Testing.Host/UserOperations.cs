using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Google;

namespace ActualChat.Testing.Host;

public static class UserOperations
{
    public static Task<AccountFull> SignInAsAlice(this IWebTester tester, string identity = "", CancellationToken cancellationToken = default)
        => tester.SignIn(NewAccount("Alice", identity), cancellationToken);

    public static Task<AccountFull> SignInAsBob(this IWebTester tester, string identity = "", CancellationToken cancellationToken = default)
        => tester.SignIn(NewAccount("Bobby", identity), cancellationToken);

    public static Task<AccountFull> SignInAsUniqueBob(this IWebTester tester, CancellationToken cancellationToken = default)
        => tester.SignInAsNew("Bobby", cancellationToken);

    public static Task<AccountFull> SignInAsUniqueAlice(this IWebTester tester, CancellationToken cancellationToken = default)
        => tester.SignInAsNew("Alice", cancellationToken);

    public static Task<AccountFull> SignInAsBobAdmin(this IWebTester tester, CancellationToken cancellationToken = default)
        => tester.SignIn(NewAdmin(), cancellationToken);

    public static Task<AccountFull> SignInAsUniqueBobAdmin(this IWebTester tester, CancellationToken cancellationToken = default)
    {
        var googleId = UniqueNames.GoogleId();
        return tester.SignIn(
            NewAdmin(email: $"bob.admin.{googleId}{Constants.Team.EmailSuffix}", googleId: googleId),
            cancellationToken);
    }

    public static AccountFull NewAccount(string name, string identity = "")
    {
        var account = new AccountFull(name).WithClaim(ClaimTypes.GivenName, name);
        return identity.IsNullOrEmpty() ? account : account.WithIdentity(identity);
    }

    public static AccountFull NewAdmin(string name = "BobAdmin", string email = $"bob{Constants.Team.EmailSuffix}", string googleId = "123")
        => new AccountFull(name)
            .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme, googleId))
            .WithClaim(ClaimTypes.Email, email);

    // Marks the user as having been last active 15 minutes ago, so presence-aware code
    // treats them as offline. Waits until UserPresences.Get observes Offline so callers
    // can rely on an immediate read returning the new state (Fusion invalidation
    // propagation is otherwise asynchronous and races with the next CreateTextEntry).
    public static async Task ForceOffline(this IWebTester tester, AccountFull user)
    {
        var clocks = tester.AppServices.Clocks();
        await tester.Commander.Call(new UserPresencesBackend_CheckIn(
            user.Id, clocks.SystemClock.Now - TimeSpan.FromMinutes(15), true));
        var presences = tester.AppServices.GetRequiredService<IUserPresences>();
        await TestExt.When(async () => {
            var presence = await presences.Get(user.Id, CancellationToken.None);
            presence.Should().Be(Presence.Offline);
        }, TimeSpan.FromSeconds(5));
    }
}
