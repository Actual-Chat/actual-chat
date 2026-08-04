using System.ComponentModel.DataAnnotations;
using Key = MessagePack.KeyAttribute;

namespace ActualChat;

/// <summary>
/// Query parameters for avatar generation endpoints.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record AvatarQuery : IValidatableObject
{
    public static readonly ImmutableArray<int> SupportedSizes = [40, 80, 160];
    public const int MaxTitleLength = 32;

    [DataMember, Key(0)]
    [Required]
    public required AvatarKind Kind { get; init; }

    [DataMember, Key(1)]
    [Required]
    public required string Key { get; init; }

    [DataMember, Key(2)]
    public AvatarFormat Format { get; init; } = AvatarFormat.Svg;

    [DataMember, Key(3)]
    public int? Size { get; init; }

    [DataMember, Key(4)]
    public string? Title { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Size is { } size && !SupportedSizes.Contains(size))
            yield return new ValidationResult(
                $"Size must be one of: {string.Join(", ", SupportedSizes)}.",
                [nameof(Size)]);

        if (Title is { } title && title.Length > MaxTitleLength)
            yield return new ValidationResult(
                $"Title must be at most {MaxTitleLength} characters long.",
                [nameof(Title)]);
    }
}
