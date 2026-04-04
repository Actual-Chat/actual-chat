using System.Linq.Expressions;
using ActualLab.Caching;

namespace ActualChat.Reflection;

public abstract class RecordTypeInfo
{
    public bool IsRecord { get; init; }
    public Func<object, object> UntypedCloner { get; init; } = null!;
}

public class RecordTypeInfo<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T> : RecordTypeInfo
{
    public readonly Func<T, T> Cloner;

#pragma warning disable IL2090
    public RecordTypeInfo()
    {
        var type = typeof(T);

        var mClone = type.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.NonPublic);
        IsRecord = mClone != null;
        if (!IsRecord)
            mClone = type.GetMethod(nameof(MemberwiseClone), BindingFlags.Instance | BindingFlags.NonPublic)!;

        var pSelf = Expression.Parameter(type, "self");
        var eBody = (Expression)Expression.Convert(Expression.Call(pSelf, mClone!), type);
        Cloner = (Func<T, T>)Expression.Lambda(eBody, pSelf).Compile();

        var pUntypedSelf = Expression.Parameter(typeof(object), "self");
        eBody = Expression.Call(Expression.Convert(pUntypedSelf, type), mClone!);
        UntypedCloner = (Func<object, object>)Expression.Lambda(eBody, pUntypedSelf).Compile();
    }
#pragma warning restore IL2090
}

public sealed class RecordTypeInfoFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    : GenericInstanceFactory, IGenericInstanceFactory<T>
{
    public override object Generate() => new RecordTypeInfo<T>();
}
