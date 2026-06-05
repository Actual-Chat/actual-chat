using System.Text.RegularExpressions;
using ActualChat.Contacts;
using ActualChat.Hosting;
using ActualChat.Text;

namespace ActualChat.Chat;

public partial class Chats
{
    [GeneratedRegex(@"^/lorem-ipsum\s+(\d+)(?:\s+(\d+)\.\.(\d+))?$")]
    private static partial Regex LoremIpsumCommandRegex();

    [GeneratedRegex(@"^/bot-army(?:\s+(\d+))?$")]
    private static partial Regex BotArmyCommandRegex();

    [GeneratedRegex(@"^/pause\s+(\d+)$")]
    private static partial Regex PauseCommandRegex();

    private async Task<ChatEntry?> TryHandleAdminCommand(
        Session session, ChatId chatId, Author author, string text,
        CancellationToken cancellationToken)
    {
        // Non-production check
        var baseUrlKind = HostInfo.BaseUrlKind;
        if (baseUrlKind is not (BaseUrlKind.Local or BaseUrlKind.Development))
            return null;

        // Admin check
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsAdmin)
            return null;

        var match = LoremIpsumCommandRegex().Match(text);
        if (match.Success)
            return await HandleLoremIpsum(chatId, author, match, cancellationToken).ConfigureAwait(false);

        match = BotArmyCommandRegex().Match(text);
        if (match.Success)
            return await HandleBotArmy(session, chatId, author, account, match, cancellationToken).ConfigureAwait(false);

        match = PauseCommandRegex().Match(text);
        if (match.Success)
            return await HandlePause(chatId, author, match, cancellationToken).ConfigureAwait(false);

        return null;
    }

    private async Task<ChatEntry> HandleLoremIpsum(
        ChatId chatId, Author author, Match match,
        CancellationToken cancellationToken)
    {
        var count = int.Parse(match.Groups[1].Value);
        count = Math.Clamp(count, 1, 500);
        var minLines = match.Groups[2].Success
            ? int.Parse(match.Groups[2].Value)
            : 1;
        var maxLines = match.Groups[3].Success
            ? int.Parse(match.Groups[3].Value)
            : 10;
        minLines = Math.Clamp(minLines, 1, 100);
        maxLines = Math.Clamp(maxLines, minLines, 100);

        ChatEntry? lastEntry = null;
        for (var i = 0; i < count; i++) {
            var lineCount = Random.Shared.Next(minLines, maxLines + 1);
            var lines = new string[lineCount];
            for (var j = 0; j < lineCount; j++) {
                var sentence = LoremIpsum.GetRandomSentence();
                // Random formatting: ~60% plain, ~15% bold, ~15% italic, ~10% code
                var roll = Random.Shared.Next(100);
                lines[j] = roll switch {
                    < 60 => sentence,
                    < 75 => $"**{sentence}**",
                    < 90 => $"*{sentence}*",
                    _ => $"`{sentence}`",
                };
            }
            var content = string.Join("\n", lines);

            var chatEntryId = ChatEntryId.New(chatId, 0);
            var upsertCommand = new ChatsBackend_ChangeEntry(
                chatEntryId,
                null,
                Change.Create(new ChatEntryDiff {
                    AuthorId = author.Id,
                    Content = content,
                }));
            lastEntry = await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
        }
        return lastEntry!;
    }

    private async Task<ChatEntry> HandlePause(
        ChatId chatId, Author author, Match match,
        CancellationToken cancellationToken)
    {
        var seconds = int.Parse(match.Groups[1].Value);
        seconds = Math.Clamp(seconds, 1, 600);
        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);

        var chatEntryId = ChatEntryId.New(chatId, 0);
        var upsertCommand = new ChatsBackend_ChangeEntry(
            chatEntryId,
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = author.Id,
                Content = $"{seconds} seconds passed.",
            }));
        return await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChatEntry> HandleBotArmy(
        Session session, ChatId chatId, Author author, AccountFull account, Match match,
        CancellationToken cancellationToken)
    {
        var count = match.Groups[1].Success
            ? int.Parse(match.Groups[1].Value)
            : 100;
        count = Math.Clamp(count, 1, 500);

        var accountsBackend = services.GetRequiredService<IAccountsBackend>();
        var myId = account.Id;

        var startIndex = 0;
        while (await accountsBackend.Get(UserId.Parse($"testbot{startIndex}"), cancellationToken).ConfigureAwait(false) != null)
            startIndex++;

        for (var i = 0; i < count; i++) {
            var userId = UserId.Parse($"testbot{startIndex + i}");
            await CreateTestBot(session, userId, cancellationToken).ConfigureAwait(false);

            var contactId = ContactId.NewUser(myId, userId);
            var contact = new Contact(contactId) { State = ContactState.Regular };
            var createCmd = new ContactsBackend_Change(contactId, null, Change.Create(contact));
            await Commander.Call(createCmd, cancellationToken).ConfigureAwait(false);
        }

        var message = $"Bot army: created {count} bots (testbot{startIndex}..testbot{startIndex + count - 1}), {count} new contacts";
        var chatEntryId = ChatEntryId.New(chatId, 0);
        var upsertCommand = new ChatsBackend_ChangeEntry(
            chatEntryId,
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = author.Id,
                Content = message,
            }));
        return await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateTestBot(Session callerSession, UserId userId, CancellationToken cancellationToken)
    {
        var accountsBackend = services.GetRequiredService<IAccountsBackend>();

        // Create & sign in the account
        var botSession = Session.New();
        var userIdentity = new UserIdentity(UserIdentity.InternalSchema, userId.Value);
        var signInCommand = new AccountsBackend_SignIn(
            botSession, userIdentity, ApiMap<UserIdentity, string>.Empty, ApiMap<string, string>.Empty,
            AutoCreate: true);
        await Commander.Call(signInCommand, cancellationToken).ConfigureAwait(false);

        // Fetch & activate
        var botAccount = await accountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        botAccount.Require();
        if (botAccount.Status != AccountStatus.Active) {
            botAccount = botAccount with { Status = AccountStatus.Active };
            var updateCommand = new AccountsBackend_Update(botAccount, botAccount.Version);
            await Commander.Call(updateCommand, cancellationToken).ConfigureAwait(false);
            botAccount = await accountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        }
        botAccount.Require(AccountFull.MustBeActive);

        // Create avatar
        var name = $"Robo {RandomNameGenerator.Default.Generate()}";
        var seed = userId.Value.GetHashCode();
        var pictureUrl = $"https://api.dicebear.com/7.x/bottts/svg?seed={seed}";
        var changeAvatarCommand = new Avatars_Change(callerSession, Symbol.Empty, null,
            Change.Create(new AvatarDiff {
                Name = name,
                Bio = $"I'm just a {name} test bot",
                PictureUrl = pictureUrl,
            }));
        var avatar = await Commander.Call(changeAvatarCommand, cancellationToken).ConfigureAwait(false);

        // Set default avatar
        var userKvas = ServerKvasBackend.ForUser(botAccount);
        var userAvatarSettings = new UserAvatarSettings() {
            DefaultAvatarId = avatar.Id,
            AvatarIds = [avatar.Id],
        };
        await userKvas.UserAvatarSettings().Set(userAvatarSettings, cancellationToken).ConfigureAwait(false);
    }
}
