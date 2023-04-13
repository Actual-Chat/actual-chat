using ActualChat.Diff.Handlers;
using ActualChat.Hosting;

namespace ActualChat.Module;

public class CoreMatchingTypeRegistry : IMatchingTypeRegistry
{
    public Dictionary<(Type Source, Symbol Scope), Type> GetMatchedTypes()
        => new () {
            {(typeof(Nullable<>), typeof(IDiffHandler).ToSymbol()), typeof(NullableDiffHandler<>)},
            {(typeof(Option<>), typeof(IDiffHandler).ToSymbol()), typeof(OptionDiffHandler<>)},
            {(typeof(SetDiff<,>), typeof(IDiffHandler).ToSymbol()), typeof(SetDiffHandler<,>)},
        };
}
