namespace ActualChat;

public interface IUnion<T> : IRequirementTarget
{
    public T Option { get; set; }
}

public interface IUnionRecord<T> : IRequirementTarget
{
    public T Option { get; init; }
}
