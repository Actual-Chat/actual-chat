namespace ActualChat.ServiceMesh;

public record ClusterLockOptions(
    TimeSpan ExpirationPeriod = default,
    float RenewalPeriod = default,
    TimeSpan CheckPeriod = default
)
{
    public virtual void AssertValid()
    {
        if (ExpirationPeriod <= TimeSpan.Zero)
            throw StandardError.Constraint<ClusterLockOptions>($"{nameof(ExpirationPeriod)} is zero or negative.");
        if (RenewalPeriod <= 0f)
            throw StandardError.Constraint<ClusterLockOptions>($"{nameof(RenewalPeriod)} is zero or negative.");
        if (CheckPeriod <= TimeSpan.Zero)
            throw StandardError.Constraint<ClusterLockOptions>($"{nameof(CheckPeriod)} is zero or negative.");
    }

    public virtual ClusterLockOptions WithDefaults(ClusterLockOptions defaults)
    {
        if (ExpirationPeriod > TimeSpan.Zero && RenewalPeriod > 0 && CheckPeriod > TimeSpan.Zero)
            return this;

        // ReSharper disable once WithExpressionModifiesAllMembers
        return this with {
            ExpirationPeriod = ExpirationPeriod > TimeSpan.Zero ? ExpirationPeriod : defaults.ExpirationPeriod,
            RenewalPeriod = RenewalPeriod > 0 ? RenewalPeriod : defaults.RenewalPeriod,
            CheckPeriod = CheckPeriod > TimeSpan.Zero ? CheckPeriod : defaults.CheckPeriod,
        };
    }
}
