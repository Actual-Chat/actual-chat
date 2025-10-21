using MemoryPack;

namespace ActualChat.Mesh;

public sealed record MeshLockOptions(
    TimeSpan ExpirationPeriod,
    float RenewalPeriodRatio = 0.4f
) {
    public static readonly MeshLockOptions Default = new(11) { ExpirationSafetyMargin = TimeSpan.FromSeconds(1) };
    public static readonly MeshLockOptions DebugFriendly = new(180);
    public static readonly MeshLockOptions TestFriendly = new(11) {
        ExpirationSafetyMargin = TimeSpan.FromSeconds(1),
        UnconditionalCheckPeriod = TimeSpan.FromSeconds(3),
    };
    public static readonly IReadOnlyDictionary<string, MeshLockOptions> Presets
        = new Dictionary<string, MeshLockOptions>(StringComparer.OrdinalIgnoreCase) {
            [nameof(Default)] = Default,
            [nameof(DebugFriendly)] = DebugFriendly,
            [nameof(TestFriendly)] = TestFriendly,
            ["Debug"] = DebugFriendly,
            ["Test"] = TestFriendly,
        };

    public TimeSpan ExpirationSafetyMargin { get; init; }
    public TimeSpan UnconditionalCheckPeriod { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan WarningDelay { get; init; } = TimeSpan.FromSeconds(15); // Negative or zero = no warning

    // Computed properties
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public TimeSpan RenewalPeriod => ExpirationPeriod * RenewalPeriodRatio;

    public MeshLockOptions(double expirationPeriod, float renewalPeriodRatio = 0.5f)
        : this(TimeSpan.FromSeconds(expirationPeriod), renewalPeriodRatio)
    { }

    public void RequireValid()
    {
        if (ExpirationPeriod <= TimeSpan.Zero)
            throw StandardError.Constraint<MeshLockOptions>($"{nameof(ExpirationPeriod)} is zero or negative.");
        if (RenewalPeriodRatio is <= 0f or >= 1f)
            throw StandardError.Constraint<MeshLockOptions>($"{nameof(RenewalPeriodRatio)} must be in (0, 1) range.");
    }
}
