using System.ComponentModel.DataAnnotations;
using ActualChat.Users.AvatarIcons;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

/// <summary>
/// Base query parameters for avatar generation endpoints.
/// </summary>
[DataContract, MemoryPackable, MessagePackObject(true)]
[MemoryPackUnion(0, typeof(BeamAvatarQuery))]
[MemoryPackUnion(1, typeof(MarbleAvatarQuery))]
public abstract partial record AvatarQueryBase : IValidatableObject
{
    [DataMember, MemoryPackOrder(0)]
    [FromRoute(Name = "key")]
    [Required]
    public required string Key { get; init; }

    [DataMember, MemoryPackOrder(1)]
    [FromQuery(Name = "format")]
    [Required]
    public AvatarFormat Format { get; init; } = AvatarFormat.Svg;

    [DataMember, MemoryPackOrder(2)]
    [FromQuery(Name = "size")]
    public int? Size { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Format == AvatarFormat.Png && Size.HasValue)
        {
            if (Size.Value != 40 && Size.Value != 80 && Size.Value != 160)
            {
                yield return new ValidationResult(
                    "Size must be 40, 80, or 160 for PNG format.",
                    new[] { nameof(Size) });
            }
        }
    }
}
