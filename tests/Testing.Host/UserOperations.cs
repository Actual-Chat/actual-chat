using System.Security.Claims;
using ActualChat.Users;
using ActualLab.Generators;
using Bunit.Extensions;
using Microsoft.AspNetCore.Authentication.Google;

namespace ActualChat.Testing.Host;

public static class UserOperations
{
    public static Task<AccountFull> SignInAsAlice(this IWebTester tester, string identity = "")
        => tester.SignIn("Alice", identity);

    public static Task<AccountFull> SignInAsBob(this IWebTester tester, string identity = "")
        => tester.SignIn("Bob", identity);

    public static Task<AccountFull> SignIn(this IWebTester tester, string name, string identity = "")
    {
        var user = new User("", name).WithClaim(ClaimTypes.GivenName, name);
        if (!identity.IsNullOrEmpty())
            user = user.WithIdentity(identity);
        return tester.SignIn(user);
    }

    public static Task<AccountFull> SignInAsUniqueBob(this IWebTester tester)
        => tester.SignInAsNew("Bob");

    public static Task<AccountFull> SignInAsUniqueAlice(this IWebTester tester)
        => tester.SignInAsNew("Alice");

    public static Task<AccountFull> SignInAsBobAdmin(this IWebTester tester)
        => tester.SignIn(NewAdmin());


    public static User NewAdmin(string name = "BobAdmin", string email = "bob@actual.chat", string googleId = "123")
        => new User("", name)
            .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme, googleId))
            .WithClaim(ClaimTypes.Email, email);
}
