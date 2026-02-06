namespace ActualChat;

/// <summary>
/// Base interface for feature flag definitions.
/// </summary>
public interface IFeatureDef
{
    public Type ResultType { get; }
    Task<object?> ComputeUntyped(IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>
/// Generic interface for feature flag definitions with typed result.
/// </summary>
public interface IFeatureDef<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    : IFeatureDef
{
    Task<T> Compute(IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>
/// Marker interface for client-side feature definitions.
/// </summary>
public interface IClientFeatureDef : IFeatureDef;

/// <summary>
/// Marker interface for server-side feature definitions.
/// </summary>
public interface IServerFeatureDef : IFeatureDef;

/// <summary>
/// Base class for feature flag definitions.
/// </summary>
public abstract class FeatureDef(Type resultType) : IFeatureDef
{
    public Type ResultType { get; } = resultType;

    Task<object?> IFeatureDef.ComputeUntyped(IServiceProvider services, CancellationToken cancellationToken)
        => EvaluateUntyped(services, cancellationToken);

    protected abstract Task<object?> EvaluateUntyped(
        IServiceProvider services,
        CancellationToken cancellationToken);
}

/// <summary>
/// Base class for feature flag definitions with typed result.
/// </summary>
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
