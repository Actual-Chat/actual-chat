using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

/// <summary>
/// Query parameters for Beam avatar generation.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record BeamAvatarQuery : AvatarQueryBase
{
}
