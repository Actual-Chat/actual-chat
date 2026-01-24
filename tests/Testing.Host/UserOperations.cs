using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Google;

namespace ActualChat.Testing.Host;

public static class UserOperations
{
    public static Task<AccountFull> SignInAsAlice(this IWebTester tester, string identity = "")
        => tester.SignIn(NewAccount("Alice", identity));

    public static Task<AccountFull> SignInAsBob(this IWebTester tester, string identity = "")
        => tester.SignIn(NewAccount("Bobby", identity));

    public static Task<AccountFull> SignInAsUniqueBob(this IWebTester tester)
        => tester.SignInAsNew("Bobby");

    public static Task<AccountFull> SignInAsUniqueAlice(this IWebTester tester)
        => tester.SignInAsNew("Alice");

    public static Task<AccountFull> SignInAsBobAdmin(this IWebTester tester)
        => tester.SignIn(NewAdmin());

    public static Task<AccountFull> SignInAsUniqueBobAdmin(this IWebTester tester)
    {
        var googleId = UniqueNames.GoogleId();
        return tester.SignIn(NewAdmin(email: $"bob.admin.{googleId}{Constants.Team.EmailSuffix}", googleId: googleId));
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
}
