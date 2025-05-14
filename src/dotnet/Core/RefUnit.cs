namespace ActualChat;

public sealed class RefUnit : IEquatable<RefUnit>
{
    public static readonly RefUnit Instance = new();
    public static readonly Task<RefUnit> InstanceTask = Task.FromResult(Instance);

    private RefUnit() { }

    public bool Equals(RefUnit? other) => other is not null;
    public override bool Equals(object? obj) => obj is RefUnit;
    public override int GetHashCode() => 1000009;
}
