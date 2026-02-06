namespace ActualChat;

/// <summary>
/// Indicates an object has an associated <see cref="NodeRef"/>.
/// </summary>
public interface IHasNodeRef
{
    NodeRef NodeRef { get; }
}
