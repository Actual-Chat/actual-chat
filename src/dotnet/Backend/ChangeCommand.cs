using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat;

// ReSharper disable once InconsistentNaming
public abstract record ChangeCommand<T, TId>(
    [property: DataMember, MemoryPackOrder(0)] TId Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<T> Change
) : ICommand<T?>, IBackendCommand, IHasShardKey<TId>
where T : IHasId<TId>, IHasVersion<long>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public TId ShardKey => Id;
}
