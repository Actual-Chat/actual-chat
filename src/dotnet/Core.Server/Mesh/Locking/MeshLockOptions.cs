using MemoryPack;

namespace ActualChat.Mesh;

public sealed record MeshLockOptions(
    TimeSpan ExpirationPeriod,
    float RenewalPeriodRatio = 0.5f
) {
    public static MeshLockOptions Default { get; set; } =
#if DEBUG
        new(TimeSpan.FromSeconds(60)) { WarningDelay = TimeSpan.FromSeconds(65) };
#else
        new(TimeSpan.FromSeconds(15)) { WarningDelay = TimeSpan.FromSeconds(20) };
#endif

    public TimeSpan UnconditionalCheckPeriod { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan WarningDelay { get; init; } // Negative or zero = no warning

    // Computed properties
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public TimeSpan RenewalPeriod => ExpirationPeriod * RenewalPeriodRatio;

    public void RequireValid()
    {
        if (ExpirationPeriod <= TimeSpan.Zero)
            throw StandardError.Constraint<MeshLockOptions>($"{nameof(ExpirationPeriod)} is zero or negative.");
        if (RenewalPeriodRatio is <= 0f or >= 1f)
            throw StandardError.Constraint<MeshLockOptions>($"{nameof(RenewalPeriodRatio)} must be in (0, 1) range.");
    }
}
