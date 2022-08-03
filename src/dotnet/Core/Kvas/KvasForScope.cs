using Stl.Reflection;

namespace ActualChat.Kvas;

public class KvasForScope<TScope> : KvasForPrefix, IKvas<TScope>
{
    public KvasForScope(IKvas upstream) : base(typeof(TScope).GetName(), upstream) { }
}
