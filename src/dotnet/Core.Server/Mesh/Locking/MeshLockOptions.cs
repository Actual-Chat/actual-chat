using MemoryPack;

namespace ActualChat.Mesh;

public sealed record MeshLockOptions(
    TimeSpan ExpirationPeriod,
    float RenewalPeriodRatio = 0.4f
) {
    public static readonly MeshLockOptions Release = new(11);
    public static readonly MeshLockOptions Debug = new(61);
    public static readonly MeshLockOptions Test = new(11) { UnconditionalCheckPeriod = TimeSpan.FromSeconds(3) };
    public static readonly MeshLockOptions Default = Debugger.IsAttached ? Debug : Release;

    public static readonly IReadOnlyDictionary<string, MeshLockOptions> Presets
        = new Dictionary<string, MeshLockOptions>(StringComparer.OrdinalIgnoreCase) {
            [nameof(Default)] = Default,
            [nameof(Debug)] = Debug,
            [nameof(Test)] = Test,
        };

    public TimeSpan ExpirationSafetyMargin { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan UnconditionalCheckPeriod { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan WarningDelay { get; init; } = TimeSpan.FromSeconds(15); // Negative or zero = no warning
    public bool LinkCancellationToken { get; init; } = true;

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
