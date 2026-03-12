using ActualLab.Caching;

namespace ActualChat.Reflection;

public static class TypeExt
{
    extension([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        public RecordTypeInfo RecordInfo => GenericInstanceCache.Get<RecordTypeInfo>(typeof(RecordTypeInfoFactory<>), type);
    }
}
