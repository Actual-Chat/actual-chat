using System.Security.Claims;

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
        phoneFactory ??= _ => UniqueNames.Phone();
        var accounts = new AccountFull[count];
        for (int i = 0; i < count; i++) {
            var phone = phoneFactory(i);
            var account = new AccountFull(userNameFactory(i))
                .WithClaim(ClaimTypes.GivenName, nameFactory(i))
                .WithClaim(ClaimTypes.Surname, secondNameFactory(i))
                .WithPhoneIdentity(phone);
            accounts[i] = await tester.SignIn(account, cancellationToken);
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
        var account = new AccountFull(name)
            .WithClaim(ClaimTypes.GivenName, name)
            .WithClaim(ClaimTypes.Surname, secondName);
        if (!email.IsNullOrEmpty())
            account = account.WithClaim(ClaimTypes.Email, email);
        if (phone != null)
            account = account.WithPhoneIdentity(phone);
        return await tester.SignIn(account);
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
        AccountFull result = null!;

        await ComputedTest.When(async ct => {
            result = await tester.Accounts.GetOwn(tester.Session, ct);
            result.Version.Should().BeGreaterThan(account.Version);
        }).WaitAsync(cancellationToken);
        return result;
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

    public static async Task<AsyncDisposable<AccountFull?>> BackupAuth(this IWebTester tester)
    {
        var accountToRestore = await tester.Accounts.GetOwn(tester.Session, CancellationToken.None);
        return AsyncDisposable.New(
            x => x != null && !x.IsGuest
                ? new ValueTask(tester.SignIn(x))
                : ValueTask.CompletedTask,
            accountToRestore.IsGuest ? null : accountToRestore);
    }
}
