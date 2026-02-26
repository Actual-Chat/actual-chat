using System.ComponentModel.DataAnnotations;
using ActualChat.Users.AvatarIcons;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

/// <summary>
/// Query parameters for avatar generation endpoints.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record AvatarQuery : IValidatableObject
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

    [DataMember, MemoryPackOrder(3)]
    [FromQuery(Name = "title")]
    public string? Title { get; init; }

    [DataMember, MemoryPackOrder(4)]
    [FromQuery(Name = "doNotBlur")]
    public bool DoNotBlur { get; init; }

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
