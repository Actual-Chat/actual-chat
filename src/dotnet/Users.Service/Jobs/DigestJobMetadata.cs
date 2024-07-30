using ActualChat.Jobs;

namespace ActualChat.Users.Jobs;

internal sealed class DigestJobMetadata : IJobMetadata
{
    public string Name => "Digest email";
    public Type JobType => typeof(DigestJob);
    public bool ExecuteAtStart => false;

    public DateTimeOffset GetNextExecutionTime(DateTimeOffset now)
    {
        var nextHour = now.AddHours(1);
        return new DateTimeOffset(
            nextHour.Year,
            nextHour.Month,
            nextHour.Day,
            nextHour.Hour,
            0,
            0,
            nextHour.Offset);
    }
}
