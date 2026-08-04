namespace ActualChat.Mesh;

public sealed record MeshLockOptions(
    TimeSpan ExpirationPeriod,
    float RenewalPeriodRatio = 0.4f
) {
    public static readonly MeshLockOptions ReleaseMeshWatcher = new(expirationPeriod: 22) {
        RenewalPeriodRatio = 0.24f,
        ExpirationSafetyMargin = TimeSpan.FromSeconds(2),
    };
    public static readonly MeshLockOptions Release = new(expirationPeriod: 11);
    public static readonly MeshLockOptions Debug = new(expirationPeriod: 122) {
        RenewalPeriodRatio = 0.082f,
    };
    public static readonly MeshLockOptions Test =  new(expirationPeriod: 21) { // GitHub build agents make huge pauses sometimes
        RenewalPeriodRatio = 0.24f,
        UnconditionalCheckPeriod = TimeSpan.FromSeconds(3),
    };
    public static readonly MeshLockOptions Default = Debugger.IsAttached ? Debug : Release;

    public static readonly IReadOnlyDictionary<string, MeshLockOptions> Presets
        = new Dictionary<string, MeshLockOptions>(StringComparer.OrdinalIgnoreCase) {
            [nameof(Default)] = Default,
            [nameof(Debug)] = Debug,
            [nameof(Test)] = Test,
        };

    public TimeSpan ExpirationSafetyMargin { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan UnconditionalCheckPeriod { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan WarningDelay { get; init; } = TimeSpan.FromSeconds(15); // Negative or zero = no warning
    public bool LinkCancellationToken { get; init; } = true;

    // Computed properties
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public TimeSpan RenewalPeriod => ExpirationPeriod * RenewalPeriodRatio;

    public MeshLockOptions(double expirationPeriod, float renewalPeriodRatio = 0.32f)
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
