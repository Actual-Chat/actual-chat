using System.ComponentModel.DataAnnotations;

namespace ActualChat;

/// <summary>
/// Query parameters for avatar generation endpoints.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record AvatarQuery : IValidatableObject
{
    [DataMember, MemoryPackOrder(0)]
    [Required]
    public required AvatarKind Kind { get; init; }

    [DataMember, MemoryPackOrder(1)]
    [Required]
    public required string Key { get; init; }

    [DataMember, MemoryPackOrder(2)]
    public AvatarFormat Format { get; init; } = AvatarFormat.Svg;

    [DataMember, MemoryPackOrder(3)]
    public int? Size { get; init; }

    [DataMember, MemoryPackOrder(4)]
    public string? Title { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Format == AvatarFormat.Png && Size.HasValue)
        {
            if (Size.Value != 40 && Size.Value != 80 && Size.Value != 160)
            {
                yield return new ValidationResult(
                    "Size must be 40, 80, or 160 for PNG format.",
                    [nameof(Size)]);
            }
        }
    }
}
