
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Engine.OpenSearch.Configuration;

internal static class ChatInfoToChatSliceRelation
{
    public static readonly string Name = JsonNamingPolicy.SnakeCaseLower.ConvertName($"{nameof(ChatInfo)}To{nameof(ChatSlice)}");
    public static readonly string ChatInfoName = JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(ChatInfo));
    public static readonly string ChatSliceName = JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(ChatSlice));
}
