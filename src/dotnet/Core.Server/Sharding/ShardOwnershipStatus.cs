namespace ActualChat.Sharding;

public enum ShardOwnershipStatus
{
    MappedToOtherNode = 0,
    MappedToThisNode,
    LockedByThisNode,
}
