namespace ActualChat.Hosting;

public interface IMatchingTypeRegistry
{
    Dictionary<(Type Source, Symbol Scope), Type> GetMatchedTypes();
}
