using ActualChat.Hosting;
using Stl.Mathematics.Internal;

namespace ActualChat.Module;

public class FusionMatchingTypeRegistry : IMatchingTypeRegistry
{
    public Dictionary<(Type Source, Symbol Scope), Type> GetMatchedTypes()
        => new () {
            {(typeof(double), typeof(IArithmetics).ToSymbol()), typeof(DoubleArithmetics)},
            {(typeof(int), typeof(IArithmetics).ToSymbol()), typeof(IntArithmetics)},
            {(typeof(long), typeof(IArithmetics).ToSymbol()), typeof(LongArithmetics)},
            {(typeof(Moment), typeof(IArithmetics).ToSymbol()), typeof(MomentArithmetics)},
            {(typeof(TimeSpan), typeof(IArithmetics).ToSymbol()), typeof(TimeSpanArithmetics)},
        };
}
