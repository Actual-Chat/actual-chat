using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

/// <summary>
/// Query parameters for Marble avatar generation.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record MarbleAvatarQuery : AvatarQueryBase
{
    [DataMember, MemoryPackOrder(3)]
    [FromQuery(Name = "title")]
    public string? Title { get; init; }

    [DataMember, MemoryPackOrder(4)]
    [FromQuery(Name = "doNotBlur")]
    public bool DoNotBlur { get; init; }
}
