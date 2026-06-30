using ActualChat.Chat;

namespace ActualChat.Testing.Host;

public static class SharedLocationOperations
{
    public static Task<SharedLocation> ReportLocation(
        this IWebTester tester,
        ChatId chatId,
        GeoPoint point,
        TimeSpan liveDuration = default,
        SharedLocationId? id = null,
        CancellationToken cancellationToken = default)
    {
        var cmd = new SharedLocations_Report(tester.Session, chatId, id, point, liveDuration);
        return tester.Commander.Call(cmd, cancellationToken);
    }

    public static async Task<ChatEntry> CreateLocationEntry(
        this IWebTester tester,
        ChatId chatId,
        GeoPoint point,
        TimeSpan liveDuration = default,
        CancellationToken cancellationToken = default)
    {
        var location = await tester.ReportLocation(chatId, point, liveDuration, cancellationToken: cancellationToken);
        var cmd = new Chats_UpsertEntry(tester.Session, chatId, null) { LocationId = location.Id };
        return await tester.Commander.Call(cmd, cancellationToken);
    }

    public static Task StopSharingLocation(
        this IWebTester tester,
        SharedLocationId id,
        CancellationToken cancellationToken = default)
        => tester.Commander.Call(new SharedLocations_Stop(tester.Session, id), cancellationToken);
}
