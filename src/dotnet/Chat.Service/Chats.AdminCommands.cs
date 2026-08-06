using System.Text.RegularExpressions;
using ActualChat.Text;

namespace ActualChat.Chat;

public partial class Chats
{
    [GeneratedRegex(@"^/lorem-ipsum\s+(\d+)(?:\s+(\d+)\.\.(\d+))?$")]
    private static partial Regex LoremIpsumCommandRegex();

    [GeneratedRegex(@"^/(?:test-users|bot-army)(?:\s+(\d+))?(?:\s+(\d+))?(?:\s+(\d+))?$")]
    private static partial Regex TestUsersCommandRegex();

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

        match = TestUsersCommandRegex().Match(text);
        if (match.Success)
            return await HandleTestUsers(session, chatId, author, account, match, cancellationToken)
                .ConfigureAwait(false);

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

    private async Task<ChatEntry> HandleTestUsers(
        Session session, ChatId chatId, Author author,
        AccountFull account, Match match, CancellationToken cancellationToken)
    {
        var userCount = ParseArgument(match, 1, 300, 1, 2000);
        var placeCount = ParseArgument(match, 2, 3, 0, 10);
        var placeSharePercent = ParseArgument(match, 3, 10, 1, 100);

        var generator = new TestDataGenerator(services);
        var options = new TestDataGenerator.Options(userCount, placeCount, placeSharePercent);
        var message = await generator.Generate(session, account, options, cancellationToken).ConfigureAwait(false);

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

    private static int ParseArgument(
        Match match, int groupIndex,
        int defaultValue, int min, int max)
    {
        var group = match.Groups[groupIndex];
        var value = group.Success ? int.Parse(group.Value) : defaultValue;
        return Math.Clamp(value, min, max);
    }
}
