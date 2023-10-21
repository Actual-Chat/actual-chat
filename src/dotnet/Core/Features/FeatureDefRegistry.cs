namespace ActualChat;

public interface IFeatureDefRegistry
{
    IFeatureDef Get(Type featureType);
}

public abstract class FeatureDefRegistry<TFeatureDef> : IFeatureDefRegistry
    where TFeatureDef : class, IFeatureDef
{
    private readonly ConcurrentDictionary<Type, TFeatureDef> _items = new();

    IFeatureDef IFeatureDefRegistry.Get(Type featureType) => Get(featureType);
    public TFeatureDef Get(Type featureType)
        => _items.GetOrAdd(featureType, static featureType1 => {
            var instance = featureType1.CreateInstance();
            if (instance is TFeatureDef featureDef)
                return featureDef;

            throw StandardError.Internal(
                $"Feature '{featureType1.GetName(true)}' is not assignable to '{typeof(TFeatureDef).GetName()}'.");
        });
}

public class ClientFeatureDefRegistry : FeatureDefRegistry<IClientFeatureDef>
{
    public static readonly ClientFeatureDefRegistry Instance = new();
}

public class ServerFeatureDefRegistry : FeatureDefRegistry<IServerFeatureDef>
{
    public static readonly ServerFeatureDefRegistry Instance = new();
}
