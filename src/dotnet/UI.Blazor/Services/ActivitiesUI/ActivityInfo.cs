namespace ActualChat.UI.Blazor.Services;

public sealed record ActivityChatInfo(ChatId Id, string Title, string PicUrl, int ExtraChatCount);

/// <summary>
/// One ongoing app activity that requires foreground state; concrete types carry
/// what the platform renderers need. <see cref="Priority"/> orders an <see cref="ActivitySet"/>.
/// </summary>
public abstract record ActivityInfo(ActivityKind Kind)
{
    public int Priority => Kind switch {
        ActivityKind.Recording => 0,
        ActivityKind.Replaying => 1,
        ActivityKind.Listening => 2,
        ActivityKind.Armed => 3,
        ActivityKind.SharingLocation => 4,
        ActivityKind.Uploading => 5,
        ActivityKind.Downloading => 6,
        _ => int.MaxValue,
    };
}

public sealed record AudioActivity(
    ActivityKind Kind,
    ActivityChatInfo Chat,
    bool IsPaused,
    bool CanPause = true,
    Moment? AnswerWindowEndsAt = null,
    bool IsStartGestureReady = false
) : ActivityInfo(Kind);

public sealed record LocationActivity(ActivityChatInfo Chat)
    : ActivityInfo(ActivityKind.SharingLocation);

public sealed record UploadActivity(
    int FileCount,
    long BytesUploaded,
    long TotalBytes,
    ImmutableList<UploadActivityItem> Items
) : ActivityInfo(ActivityKind.Uploading)
{
    public double Progress => TotalBytes == 0 ? 0 : (double)BytesUploaded / TotalBytes;
    public bool Equals(UploadActivity? other)
        => other is not null
            && FileCount == other.FileCount
            && BytesUploaded == other.BytesUploaded
            && TotalBytes == other.TotalBytes
            && Items.SequenceEqual(other.Items);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(FileCount);
        hash.Add(BytesUploaded);
        hash.Add(TotalBytes);
        foreach (var item in Items)
            hash.Add(item);
        return hash.ToHashCode();
    }
}

public sealed record UploadActivityItem(
    string SessionId,
    string FileName,
    long BytesUploaded,
    long TotalBytes)
{
    public double Progress => TotalBytes == 0 ? 0 : (double)BytesUploaded / TotalBytes;
}
