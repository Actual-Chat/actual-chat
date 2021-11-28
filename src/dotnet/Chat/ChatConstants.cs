namespace ActualChat.Chat;

public static class ChatConstants
{
    public static string DefaultChatId { get; } = "the-actual-one";
    public static TileStack<long> IdTileStack { get; } = Constants.TileStacks.Long16To1K;
    public static TileStack<Moment> TimeTileStack { get; } = Constants.TileStacks.Moment3MTo6Y;
    public static TimeSpan MaxEntryDuration { get; } = TimeTileStack.MinTileSize.EpochOffset; // 3 minutes, though it can be any
}
