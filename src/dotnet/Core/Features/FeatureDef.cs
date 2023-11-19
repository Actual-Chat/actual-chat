using System.Diagnostics.CodeAnalysis;

namespace ActualChat;

public interface IFeatureDef
{
    public Type ResultType { get; }
    Task<object?> ComputeUntyped(IServiceProvider services, CancellationToken cancellationToken);
}

public interface IFeatureDef<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    : IFeatureDef
{
    Task<T> Compute(IServiceProvider services, CancellationToken cancellationToken);
}

public interface IClientFeatureDef : IFeatureDef { }
public interface IServerFeatureDef : IFeatureDef { }

public abstract class FeatureDef(Type resultType) : IFeatureDef
{
    public Type ResultType { get; } = resultType;

    Task<object?> IFeatureDef.ComputeUntyped(IServiceProvider services, CancellationToken cancellationToken)
        => EvaluateUntyped(services, cancellationToken);

    protected abstract Task<object?> EvaluateUntyped(
        IServiceProvider services,
        CancellationToken cancellationToken);
}

public abstract class FeatureDef<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    : FeatureDef, IFeatureDef<T>
{
    protected FeatureDef() : base(typeof(T)) { }

    protected override async Task<object?> EvaluateUntyped(
        IServiceProvider services,
        CancellationToken cancellationToken)
        => await Compute(services, cancellationToken).ConfigureAwait(false);

    public abstract Task<T> Compute(IServiceProvider services, CancellationToken cancellationToken);
}
