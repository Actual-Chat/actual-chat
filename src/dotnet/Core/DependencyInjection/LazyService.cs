using System.Diagnostics.CodeAnalysis;

namespace ActualChat.DependencyInjection;

public class LazyService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]T>: Lazy<T>
    where T : class
{
    public LazyService(IServiceProvider services)
        : base(services.GetRequiredService<T>)
    { }
}
