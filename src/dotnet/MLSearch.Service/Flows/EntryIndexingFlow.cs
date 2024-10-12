using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

// [DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// public partial class EntryIndexingFlow : PeriodicFlow
// {
//     [IgnoreDataMember, MemoryPackIgnore]
//     protected ChatId ChatId { get; set; }
//
//     protected override async Task<string?> Update(CancellationToken cancellationToken)
//     {
//         var chatId = ChatId.Parse(Id.Arguments);
//         ChatId = chatId;
//         return null;
//     }
//
//     protected override async Task Run(CancellationToken cancellationToken)
//     {
//         var indexer = Host.Services.GetRequiredService<EntryIndexer>();
//         await indexer.Index(ChatId, cancellationToken).ConfigureAwait(false);
//     }
//
//     protected override Moment ComputeNextRunAt(Moment now, CancellationToken cancellationToken)
//         => throw new NotImplementedException();
// }

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class EntryIndexingFlow : Flow
{
    [IgnoreDataMember, MemoryPackIgnore]
    protected ChatId ChatId { get; set; }

    protected override async Task<string?> Update(CancellationToken cancellationToken)
    {
        var chatId = ChatId.Parse(Id.Arguments);
        ChatId = chatId;
        return null;
    }

    protected override async Task Run(CancellationToken cancellationToken)
    {
        var indexer = Host.Services.GetRequiredService<EntryIndexer>();
        await indexer.Index(ChatId, cancellationToken).ConfigureAwait(false);
    }

    protected override Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        var chatId = ChatId.Parse(Id.Arguments);
        ChatId = chatId;
    }
}
