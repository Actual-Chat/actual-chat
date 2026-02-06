namespace ActualChat;

/// <summary>
/// Registry for feature definitions indexed by type.
/// </summary>
public interface IFeatureDefRegistry
{
    IFeatureDef Get([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type featureType);
}

/// <summary>
/// Base class for typed feature definition registries.
/// </summary>
public abstract class FeatureDefRegistry<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TFeatureDef> : IFeatureDefRegistry
    where TFeatureDef : class, IFeatureDef
{
    private readonly ConcurrentDictionary<Type, TFeatureDef> _items = new();

    IFeatureDef IFeatureDefRegistry.Get(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type featureType)
        => Get(featureType);

    public TFeatureDef Get(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type featureType)
    {
        [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Covered by DynamicallyAccessedMemberTypes.All above")]
        static TFeatureDef FeatureDefFactory(Type featureType1) {
            var instance = featureType1.CreateInstance();
            if (instance is TFeatureDef featureDef)
                return featureDef;

            throw StandardError.Internal($"Feature '{featureType1.GetName(true)}' is not assignable to '{typeof(TFeatureDef).GetName()}'.");
        }

        return _items.GetOrAdd(featureType, FeatureDefFactory);
    }
}

/// <summary>
/// Registry for client-side feature definitions.
/// </summary>
public class ClientFeatureDefRegistry : FeatureDefRegistry<IClientFeatureDef>
{
    public static readonly ClientFeatureDefRegistry Instance = new();
}

/// <summary>
/// Registry for server-side feature definitions.
/// </summary>
public class ServerFeatureDefRegistry : FeatureDefRegistry<IServerFeatureDef>
{
    public static readonly ServerFeatureDefRegistry Instance = new();
}
