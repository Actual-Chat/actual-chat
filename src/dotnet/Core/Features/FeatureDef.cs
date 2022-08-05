namespace ActualChat;

public interface IFeatureDef
{
    public Type ResultType { get; }
    Task<object?> ComputeUntyped(IServiceProvider services, CancellationToken cancellationToken);
}

public interface IFeatureDef<T> : IFeatureDef
{
    Task<T> Compute(IServiceProvider services, CancellationToken cancellationToken);
}

public interface IClientFeatureDef : IFeatureDef { }
public interface IServerFeatureDef : IFeatureDef { }

public abstract class FeatureDef : IFeatureDef
{
    public Type ResultType { get; }

    protected FeatureDef(Type resultType)
        => ResultType = resultType;

    Task<object?> IFeatureDef.ComputeUntyped(IServiceProvider services, CancellationToken cancellationToken)
        => EvaluateUntyped(services, cancellationToken);

    protected abstract Task<object?> EvaluateUntyped(
        IServiceProvider services,
        CancellationToken cancellationToken);
}

public abstract class FeatureDef<T> : FeatureDef, IFeatureDef<T>
{
    protected FeatureDef() : base(typeof(T)) { }

    protected override async Task<object?> EvaluateUntyped(
        IServiceProvider services,
        CancellationToken cancellationToken)
        => await Compute(services, cancellationToken).ConfigureAwait(false);

    public abstract Task<T> Compute(IServiceProvider services, CancellationToken cancellationToken);
}
