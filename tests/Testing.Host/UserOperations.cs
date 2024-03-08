using System.Security.Claims;
using ActualChat.Users;
using Microsoft.AspNetCore.Authentication.Google;

namespace ActualChat.Testing.Host;

public static class UserOperations
{
    public static Task<AccountFull> SignInAsAlice(this IWebTester tester)
        => tester.SignIn(new User("", "Alice"));

    public static Task<AccountFull> SignInAsBob(this IWebTester tester, string identity = "")
    {
        var user = new User("", "Bob");
        if (!identity.IsNullOrEmpty())
            user = user.WithIdentity(identity);
        return tester.SignIn(user);
    }

    public static Task<AccountFull> SignInAsBobAdmin(this IWebTester tester)
        => tester.SignIn(NewAdmin());

    public static User NewAdmin(string name = "BobAdmin", string email = "bob@actual.chat", string googleId = "123")
        => new User("", name)
            .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme, googleId))
            .WithClaim(ClaimTypes.Email, email);
}
