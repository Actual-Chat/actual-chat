using System.ComponentModel.DataAnnotations;
using ActualChat.Users.AvatarIcons;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

public class BeamAvatarQuery : IValidatableObject
{
    [FromRoute(Name = "key")]
    public string Key { get; set; } = "";

    [FromQuery(Name = "format")]
    public AvatarFormat Format { get; set; } = AvatarFormat.Svg;

    [FromQuery(Name = "size")]
    public int? Size { get; set; }

    [FromQuery(Name = "square")]
    public bool Square { get; set; }

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
