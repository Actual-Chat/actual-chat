using System.Text.Json.Serialization.Metadata;
using ActualChat.MLSearch.Documents;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch.Serializer;

internal static class ChatsTypeInfoModifier
{
    private static readonly Type ChatInfoType = typeof(ChatInfo);
    private static readonly Type ChatSliceType = typeof(ChatSlice);

    public static void Modify(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type == ChatInfoType) {
            var joinProperty = typeInfo.CreateJsonPropertyInfo(typeof(JoinField), "join");
            joinProperty.Get = _ => JoinField.Root<ChatInfo>();
            typeInfo.Properties.Add(joinProperty);
        }
        if (typeInfo.Type == ChatSliceType) {
            var joinProperty = typeInfo.CreateJsonPropertyInfo(typeof(JoinField), "join");
            joinProperty.Get = o => {
                var chatSlice = (ChatSlice)o;
                return JoinField.Link<ChatSlice>((string)chatSlice.Metadata.ChatId);
            };
            var routingProperty = typeInfo.CreateJsonPropertyInfo(typeof(string), "_routing");
            routingProperty.Get = o => {
                var chatSlice = (ChatSlice)o;
                return (string)chatSlice.Metadata.ChatId;
            };

            typeInfo.Properties.AddRange([joinProperty, routingProperty]);
        }
    }
}
