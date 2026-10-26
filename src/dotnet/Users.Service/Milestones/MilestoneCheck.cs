namespace ActualChat.Users;

public readonly record struct MilestoneCheck(MilestoneCheckKind Kind, string Note = "", Moment? RetryAt = null)
{
    public static MilestoneCheck Send { get; } = new(MilestoneCheckKind.Send);

    public static MilestoneCheck Skip(string note)
        => new(MilestoneCheckKind.Skip, note);

    public static MilestoneCheck Wait(string note, Moment retryAt)
        => new(MilestoneCheckKind.Wait, note, retryAt);
}
