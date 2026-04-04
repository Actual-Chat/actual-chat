using ActualChat.Aot;

namespace ActualChat.App.AotHelper;

public interface IAotTypeTester
{
    AotTypeKind Kind { get; }
    bool Test(Type type);
}
