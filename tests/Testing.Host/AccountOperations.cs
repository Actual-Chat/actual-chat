using System.Security.Claims;
using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.Testing.Host;

public static class AccountOperations
{
    public static Task<AccountFull> GetOwnAccount(this IWebTester tester, CancellationToken cancellationToken = default)
        => tester.Accounts.GetOwn(tester.Session, cancellationToken);

    public static async Task<AccountFull[]> CreateAccounts(
        this IWebTester tester,
        int count,
        Func<int, string>? userNameFactory = null,
        Func<int, string>? nameFactory = null,
        Func<int, string>? secondNameFactory = null)
    {
        await using var __ = await tester.BackupAuth();
        userNameFactory ??= UniqueNames.User;
        nameFactory ??= _ => "User";
        secondNameFactory ??= i => $"{i}";
        var accounts = new AccountFull[count];
        for (int i = 0; i < count; i++) {
            var user = new User("", userNameFactory(i)).WithClaim(ClaimTypes.GivenName, nameFactory(i))
                .WithClaim(ClaimTypes.Surname, secondNameFactory(i));
            accounts[i] = await tester.SignIn(user);
        }
        return accounts;
    }

    public static async Task<AccountFull[]> CreateAccounts(
        this IWebTester tester,
        params AccountFull[] accounts)
    {
        await using var __ = await tester.BackupAuth();
        var createdAccounts = new AccountFull[accounts.Length];
        for (var i = 0; i < accounts.Length; i++)
            createdAccounts[i] = await tester.SignIn(ToUser(accounts[i]));
        return createdAccounts;
        // return await accounts.Select(x => tester.SignIn(ToUser(x))).Collect(1);

        User ToUser(AccountFull account)
            => account.User.WithClaim(ClaimTypes.GivenName, account.Name)
                .WithClaim(ClaimTypes.Surname, account.LastName);
    }

    public static async Task<AccountFull> CreateAccount(
        this IWebTester tester,
        string name,
        string secondName = "",
        string email = "",
        Phone phone = default)
    {
        await using var __ = await tester.BackupAuth();
        var user = new User("", name).WithClaim(ClaimTypes.GivenName, name)
            .WithClaim(ClaimTypes.Surname, secondName);
        if (email.IsNullOrEmpty())
            user = user.WithClaim(ClaimTypes.Email, email);
        if (!phone.IsNone)
            user = user.WithPhone(phone);
        return await tester.SignIn(user);
    }

    public static async Task<AsyncDisposable<User?>> BackupAuth(this IWebTester tester)
    {
        var userToRestore = await tester.Auth.GetUser(tester.Session);
        return AsyncDisposable.New(x => x != null ? tester.SignIn(x).ToVoidValueTask() : ValueTask.CompletedTask, userToRestore);
    }
}
