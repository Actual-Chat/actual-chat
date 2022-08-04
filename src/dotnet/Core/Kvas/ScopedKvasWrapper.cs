using Stl.Reflection;

namespace ActualChat.Kvas;

public class ScopedKvasWrapper<TScope> : PrefixedKvasWrapper, IKvas<TScope>
{
    public ScopedKvasWrapper(IKvas upstream) : base(upstream, typeof(TScope).GetName()) { }
}
