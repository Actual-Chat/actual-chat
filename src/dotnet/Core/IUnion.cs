namespace ActualChat;

/// <summary>
/// Interface for discriminated union types with mutable option.
/// </summary>
public interface IUnion<T> : IRequirementTarget
{
    public T Option { get; set; }
}

/// <summary>
/// Interface for discriminated union records with immutable option.
/// </summary>
public interface IUnionRecord<T> : IRequirementTarget
{
    public T Option { get; init; }
}
