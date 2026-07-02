namespace ActualChat.Testing.Host;

public static class SharedLocationOperations
{
    public static async Task<SharedLocation> ReportLocation(
        this IWebTester tester,
        ChatId chatId,
        GeoPoint point,
        TimeSpan liveDuration = default,
        SharedLocationId? id = null,
        CancellationToken cancellationToken = default)
    {
        var change = id is null
            ? Change.Create(new SharedLocationDiff { Point = point, LiveDuration = liveDuration })
            : Change.Update(new SharedLocationDiff { Point = point });
        var cmd = new SharedLocations_Change(tester.Session, chatId, id, change);
        var result = await tester.Commander.Call(cmd, cancellationToken);
        return result!;
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
        ChatId chatId,
        SharedLocationId id,
        CancellationToken cancellationToken = default)
    {
        var cmd = new SharedLocations_Change(tester.Session, chatId, id, Change.Remove<SharedLocationDiff>());
        return tester.Commander.Call(cmd, cancellationToken);
    }
}
