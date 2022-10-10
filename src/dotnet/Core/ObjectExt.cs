using System.Linq.Expressions;
using System.Reflection;
using Stl.Reflection;

namespace ActualChat;

public static class ObjectExt
{
    private static readonly ConcurrentDictionary<Type, Info> CachedInfo = new();

    public static bool IsRecord<T>() => GetInfo<T>().IsRecord;
    public static bool IsRecord(Type type) => GetInfo(type).IsRecord;

    public static Func<T, T> GetCloner<T>() => GetInfo<T>().Cloner;
    public static Func<object, object> GetCloner(Type type) => GetInfo(type).UntypedCloner;

    public static T Clone<T>(T source) => GetCloner<T>().Invoke(source);
    public static object? Clone(object? source) => source == null ? null : GetCloner(source.GetType()).Invoke(source);

    // Private methods

    private static Info<T> GetInfo<T>()
        => (Info<T>) GetInfo(typeof(T));
    private static Info GetInfo(Type type)
        => CachedInfo.GetOrAdd(type, static type1 => (Info) typeof(Info<>).MakeGenericType(type1).CreateInstance());

    // Nested types

    private class Info
    {
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public bool IsRecord;
        public Func<object, object> UntypedCloner = null!;
    }

    private class Info<T> : Info
    {
        public readonly Func<T, T> Cloner;

#pragma warning disable IL2090
        public Info()
        {
            var type = typeof(T);

            var mClone = type.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.NonPublic);
            IsRecord = mClone != null;
            if (!IsRecord)
                mClone = type.GetMethod(nameof(MemberwiseClone), BindingFlags.Instance | BindingFlags.NonPublic)!;

            var pSelf = Expression.Parameter(type, "self");
            var eBody = (Expression) Expression.Convert(Expression.Call(pSelf, mClone!), type);
            Cloner = (Func<T, T>) Expression.Lambda(eBody, pSelf).Compile();

            var pUntypedSelf = Expression.Parameter(typeof(object), "self");
            eBody = Expression.Call(Expression.Convert(pUntypedSelf, type), mClone!);
            UntypedCloner = (Func<object, object>) Expression.Lambda(eBody, pUntypedSelf).Compile();
        }
#pragma warning restore IL2090
    }
}
