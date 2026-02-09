using System.Text.RegularExpressions;
using ActualChat.Hosting;
using ActualChat.Text;

namespace ActualChat.Chat;

public partial class Chats
{
    [GeneratedRegex(@"^/lorem-ipsum\s+(\d+)(?:\s+(\d+)\.\.(\d+))?$")]
    private static partial Regex LoremIpsumCommandRegex();

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
        if (!match.Success)
            return null;

        var count = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        count = Math.Clamp(count, 1, 500);
        var minLines = match.Groups[2].Success
            ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
            : 1;
        var maxLines = match.Groups[3].Success
            ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)
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

            var textEntryId = TextEntryId.New(chatId, 0);
            var upsertCommand = new ChatsBackend_ChangeEntry(
                textEntryId,
                null,
                Change.Create(new ChatEntryDiff {
                    AuthorId = author.Id,
                    Content = content,
                }));
            lastEntry = await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
        }
        return lastEntry!;
    }
}
