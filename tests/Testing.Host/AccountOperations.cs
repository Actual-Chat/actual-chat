using System.Security.Claims;
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
        Func<int, string>? secondNameFactory = null,
        Func<int, Phone>? phoneFactory = null,
        CancellationToken cancellationToken = default)
    {
        await using var __ = await tester.BackupAuth();
        userNameFactory ??= UniqueNames.User;
        nameFactory ??= _ => "User";
        secondNameFactory ??= i => $"{i}";
        phoneFactory ??= i => UniqueNames.Phone();
        var accounts = new AccountFull[count];
        for (int i = 0; i < count; i++) {
            var user = new User("", userNameFactory(i)).WithClaim(ClaimTypes.GivenName, nameFactory(i))
                .WithClaim(ClaimTypes.Surname, secondNameFactory(i))
                .WithPhone(phoneFactory(i));
            accounts[i] = await tester.SignIn(user, cancellationToken);
        }
        return accounts;
    }

    public static async Task<AccountFull> CreateAccount(
        this IWebTester tester,
        string name,
        string secondName = "",
        string email = "",
        Phone? phone = null)
    {
        await using var __ = await tester.BackupAuth();
        var user = new User("", name).WithClaim(ClaimTypes.GivenName, name)
            .WithClaim(ClaimTypes.Surname, secondName);
        if (email.IsNullOrEmpty())
            user = user.WithClaim(ClaimTypes.Email, email);
        if (phone != null)
            user = user.WithPhone(phone);
        return await tester.SignIn(user);
    }

    public static async Task<AccountFull> UpdateAccount(
        this IWebTester tester,
        AccountFull account,
        CancellationToken cancellationToken = default)
    {
        await using var __ = await tester.BackupAuth();
        await tester.SignIn(account, cancellationToken);
        var cmd = new Accounts_Update(tester.Session, account, null);
        await tester.Commander.Call(cmd, cancellationToken);
        return await tester.Accounts.GetOwn(tester.Session, cancellationToken);
    }

    public static async Task<AccountFull> DeleteAccount(
        this IWebTester tester,
        AccountFull account,
        CancellationToken cancellationToken = default)
    {
        await using var __ = await tester.BackupAuth();
        await tester.SignIn(account, cancellationToken);
        var cmd = new Accounts_DeleteOwn(tester.Session);
        await tester.Commander.Call(cmd, cancellationToken);
        return await tester.Accounts.GetOwn(tester.Session, cancellationToken);
    }

    public static async Task<AsyncDisposable<User?>> BackupAuth(this IWebTester tester)
    {
        var userToRestore = await tester.Auth.GetUser(tester.Session);
        return AsyncDisposable.New(
            x => x != null
                ? new ValueTask(tester.SignIn(x))
                : ValueTask.CompletedTask,
            userToRestore);
    }
}
