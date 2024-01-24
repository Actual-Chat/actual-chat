using MemoryPack;

namespace ActualChat.Mesh;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record MeshLockOptions(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] TimeSpan ExpirationPeriod,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] float RenewalPeriodRatio = 0.5f
) {
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public TimeSpan RenewalPeriod => ExpirationPeriod * RenewalPeriodRatio;

    public virtual void AssertValid()
    {
        if (ExpirationPeriod <= TimeSpan.Zero)
            throw StandardError.Constraint<MeshLockOptions>($"{nameof(ExpirationPeriod)} is zero or negative.");
        if (RenewalPeriodRatio is <= 0f or >= 1f)
            throw StandardError.Constraint<MeshLockOptions>($"{nameof(RenewalPeriodRatio)} must be in (0, 1) range.");
    }
}
