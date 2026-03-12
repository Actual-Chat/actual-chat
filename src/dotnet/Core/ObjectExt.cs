using ActualChat.Reflection;

namespace ActualChat;

#pragma warning disable IL2072

public static class ObjectExt
{
    public static T Clone<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T source)
        => ((RecordTypeInfo<T>)typeof(T).RecordInfo).Cloner.Invoke(source);
    public static object? Clone(object? source)
        => source?.GetType().RecordInfo.UntypedCloner.Invoke(source);

}
