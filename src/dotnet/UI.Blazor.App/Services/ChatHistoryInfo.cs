namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Where a chat's history starts: its first message and the id range around it.
/// </summary>
public sealed record ChatHistoryInfo
{
    public static readonly ChatHistoryInfo None = new();

    public ChatEntry? First { get; init; }
    public Range<long> IdRange { get; init; }
}
