using System.Security.Claims;
using ActualChat.Users;

namespace ActualChat.Testing.Host;

public static class AccountOperations
{
    public static async Task<AccountFull[]> CreateAccounts(
        this IWebClientTester tester,
        int count,
        Func<int, string>? userNameFactory = null,
        Func<int, string>? nameFactory = null,
        Func<int, string>? secondNameFactory = null)
    {
        var userToRestore = await tester.Auth.GetUser(tester.Session);
        userNameFactory ??= i => $"User {i}";
        nameFactory ??= _ => "User";
        secondNameFactory ??= i => $"{i}";
        var accounts = new AccountFull[count];
        for (int i = 0; i < count; i++) {
            var user = new User("", userNameFactory(i)).WithClaim(ClaimTypes.GivenName, nameFactory(i))
                .WithClaim(ClaimTypes.Surname, secondNameFactory(i));
            accounts[i] = await tester.SignIn(user);
        }
        if (userToRestore != null)
            await tester.SignIn(userToRestore);
        return accounts;
    }
}
