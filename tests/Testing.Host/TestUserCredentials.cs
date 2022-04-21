using System.ComponentModel.DataAnnotations;

namespace ActualChat.Testing.Host;

public class TestUserCredentials
{
    [Required] public string Email { get; set; } = null!;
    [Required] public string Password { get; set; } = null!;
}
